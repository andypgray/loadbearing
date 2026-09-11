using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Building;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;
using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing.Hosting;

/// <summary>
///     Turns <see cref="IArchitectureSpec" /> implementations into an <see cref="ArchitectureModel" />:
///     runs each spec's <c>Define</c> against one shared <see cref="Arch" />, reports everything wrong
///     with the result together, and hands back the finished model. The way in for anything hosting
///     LoadBearing — the CLI, the test adapter, a tool of your own.
/// </summary>
public static class ArchModelBuilder
{
    /// <summary>
    ///     Builds the model from the given specs. They share one <see cref="Arch" />, so their rule, scope
    ///     and layer names must be unique across all of them, and each spec's <c>Define</c> runs in the order
    ///     given. Nothing is checked against any code here: the result is the spec as data.
    /// </summary>
    /// <exception cref="SpecValidationException">
    ///     Something in the specs is wrong. Every mistake found is in
    ///     <see cref="SpecValidationException.Errors" />, not only the first.
    /// </exception>
    public static ArchitectureModel Build(params IArchitectureSpec[] specs)
    {
        return Build((IEnumerable<IArchitectureSpec>)specs);
    }

    /// <summary>
    ///     Builds the model from the given specs. They share one <see cref="Arch" />, so their rule, scope
    ///     and layer names must be unique across all of them, and each spec's <c>Define</c> runs in the order
    ///     the sequence yields it. Nothing is checked against any code here: the result is the spec as data.
    /// </summary>
    /// <exception cref="SpecValidationException">
    ///     Something in the specs is wrong. Every mistake found is in
    ///     <see cref="SpecValidationException.Errors" />, not only the first.
    /// </exception>
    public static ArchitectureModel Build(IEnumerable<IArchitectureSpec> specs)
    {
        var arch = new Arch();
        foreach (IArchitectureSpec spec in Guard.NotNull(specs, nameof(specs))) Guard.NotNull(spec, nameof(spec)).Define(arch);

        IReadOnlyList<SpecValidationError> errors = SpecValidator.Validate(arch);
        if (errors.Count > 0) throw new SpecValidationException(errors);

        return ProjectModel(arch);
    }

    private static ArchitectureModel ProjectModel(Arch arch)
    {
        var rules = new List<ArchRule>();
        foreach (Registration registration in arch.Registrations)
            switch (registration)
            {
                case RuleRegistration rule:
                    rules.Add(ProjectRule(rule));
                    break;
                case ScopeRegistration scope:
                    rules.AddRange(ScopeDesugarer.Desugar(scope));
                    break;
            }

        List<LayerDefinition> layers = arch.Layers.Select(ProjectLayer).ToList();
        return new ArchitectureModel(rules, layers);
    }

    private static ArchRule ProjectRule(RuleRegistration rule)
    {
        Constraint constraint = rule.Constraint!;
        string sentence = SentenceRenderer.Sentence(constraint);
        string because = rule.Becauses.FirstOrDefault() ?? string.Empty;
        string? fix = rule.Fixes.FirstOrDefault();
        string? citation = rule.Citations.FirstOrDefault();

        if (rule.Posture == Posture.Migrate)
        {
            // .Baseline(path) omitted ⇒ the conventional default derived from the rule ID (GRAMMAR §4.4),
            // so MigrateData.BaselinePath is never null post-build.
            string baseline = rule.Baselines.FirstOrDefault() ?? BaselineConventions.DefaultPath(rule.Id);
            MigrationPolicy policy = rule.Policies.Count > 0 ? rule.Policies[0] : MigrationPolicy.MigrateIfSmall;
            var migrate = new MigrateData(rule.MigrateFrom ?? string.Empty, sentence, baseline, policy);
            return new ArchRule(rule.Id, Posture.Migrate, because, fix, sentence, constraint, migrate, null, citation);
        }

        return new ArchRule(rule.Id, Posture.Enforce, because, fix, sentence, constraint, null, null, citation);
    }

    private static LayerDefinition ProjectLayer(LayerRegistration registration)
    {
        LayerNoun noun = registration.Noun;
        string? purpose = registration.Purposes.FirstOrDefault();
        return new LayerDefinition(
            noun.Name, noun.Globs, SentenceRenderer.LayerDefinition(noun, purpose), purpose, noun.Definition);
    }
}
