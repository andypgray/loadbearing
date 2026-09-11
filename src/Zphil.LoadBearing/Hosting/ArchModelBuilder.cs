using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Building;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;
using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing.Hosting;

/// <summary>
///     Finalizes one or more specs into a walkable <see cref="ArchitectureModel" />: mint a fresh
///     <see cref="Arch" />, run each spec's <see cref="IArchitectureSpec.Define" />, run the whole
///     validation catalog (throwing an aggregate <see cref="SpecValidationException" /> on any
///     error), then desugar scopes and project the read model (GRAMMAR §7, §8).
/// </summary>
public static class ArchModelBuilder
{
    /// <summary>Builds a model from the given specs (one shared <see cref="Arch" />).</summary>
    /// <exception cref="SpecValidationException">The specs failed one or more validation checks.</exception>
    public static ArchitectureModel Build(params IArchitectureSpec[] specs)
    {
        return Build((IEnumerable<IArchitectureSpec>)specs);
    }

    /// <summary>Builds a model from the given specs (one shared <see cref="Arch" />).</summary>
    /// <exception cref="SpecValidationException">The specs failed one or more validation checks.</exception>
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
