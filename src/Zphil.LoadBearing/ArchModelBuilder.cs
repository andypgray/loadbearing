using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Building;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;
using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing;

/// <summary>
///     Finalizes one or more specs into a walkable <see cref="ArchitectureModel" />: mint a fresh
///     <see cref="Arch" />, run each spec's <see cref="IArchitectureSpec.Define" />, run the whole
///     validation catalog (throwing an aggregate <see cref="SpecValidationException" /> on any
///     error), then desugar Freeze scopes and project the read model (GRAMMAR §7, §8).
/// </summary>
public static class ArchModelBuilder
{
    /// <summary>Builds a model from the given specs (one shared <see cref="Arch" />).</summary>
    public static ArchitectureModel Build(params IArchitectureSpec[] specs)
    {
        return Build((IEnumerable<IArchitectureSpec>)specs);
    }

    /// <summary>Builds a model from the given specs (one shared <see cref="Arch" />).</summary>
    public static ArchitectureModel Build(IEnumerable<IArchitectureSpec> specs)
    {
        var arch = new Arch();
        foreach (IArchitectureSpec spec in Guard.NotNull(specs, nameof(specs))) Guard.NotNull(spec, nameof(spec)).Define(arch);

        var errors = SpecValidator.Validate(arch);
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
                    rules.AddRange(FreezeDesugarer.Desugar(scope));
                    break;
            }

        var layers = arch.Layers.Select(ProjectLayer).ToList();
        return new ArchitectureModel(rules, layers);
    }

    private static ArchRule ProjectRule(RuleRegistration rule)
    {
        Constraint constraint = rule.Constraint!;
        string sentence = SentenceRenderer.Sentence(constraint);
        string because = rule.Becauses.Count > 0 ? rule.Becauses[0] : string.Empty;
        string? fix = rule.Fixes.Count > 0 ? rule.Fixes[0] : null;

        if (rule.Posture == Posture.Migrate)
        {
            // .Baseline(path) omitted ⇒ the conventional default derived from the rule ID (GRAMMAR §4.4),
            // so MigrateData.BaselinePath is never null post-build.
            string baseline = rule.Baselines.Count > 0 ? rule.Baselines[0] : BaselineConventions.DefaultPath(rule.Id);
            MigrationPolicy policy = rule.Policies.Count > 0 ? rule.Policies[0] : MigrationPolicy.MigrateIfSmall;
            var migrate = new MigrateData(rule.MigrateFrom ?? string.Empty, sentence, baseline, policy);
            return new ArchRule(rule.Id, Posture.Migrate, because, fix, sentence, constraint, migrate, null);
        }

        return new ArchRule(rule.Id, Posture.Enforce, because, fix, sentence, constraint, null, null);
    }

    private static LayerDefinition ProjectLayer(LayerNoun noun)
    {
        return new LayerDefinition(noun.Name, noun.Globs, SentenceRenderer.LayerDefinition(noun));
    }
}