namespace Zphil.LoadBearing.Hosting;

/// <summary>
///     One reified rule: a stable ID, a posture, rationale, an optional fix, the
///     deterministic law <see cref="Sentence" />, and the posture-specific payload. Scopes desugar into
///     <see cref="Posture.Quarantine" />/<see cref="Posture.Caution" /> rules carrying
///     <see cref="Scope" /> (GRAMMAR §7).
/// </summary>
public sealed class ArchRule
{
    internal ArchRule(
        string id,
        Posture posture,
        string because,
        string? fix,
        string sentence,
        Constraint? constraint,
        MigrateData? migrate,
        ScopeData? scope)
    {
        Id = id;
        Posture = posture;
        Because = because;
        Fix = fix;
        Sentence = sentence;
        Constraint = constraint;
        Migrate = migrate;
        Scope = scope;
    }

    /// <summary>The stable rule ID (baseline key, message citation, <c>arch_explain</c> handle).</summary>
    public string Id { get; }

    /// <summary>The lifecycle posture.</summary>
    public Posture Posture { get; }

    /// <summary>The rationale, rendered to agents and echoed in violation messages.</summary>
    public string Because { get; }

    /// <summary>The remediation hint, or null when none was supplied.</summary>
    public string? Fix { get; }

    /// <summary>
    ///     The rendered law sentence (GRAMMAR §6). Empty for a scope tripwire, which carries no
    ///     closed-vocabulary constraint — it is a diff-aware touch check, not a law (GRAMMAR §7).
    /// </summary>
    public string Sentence { get; }

    /// <summary>
    ///     The checkable constraint — the <c>Enforce</c> constraint, the Migrate <c>to</c> target,
    ///     or the Quarantine containment predicate. Null for a scope tripwire.
    /// </summary>
    public Constraint? Constraint { get; }

    /// <summary>Migrate-specific payload, or null for non-Migrate rules.</summary>
    public MigrateData? Migrate { get; }

    /// <summary>Scope-specific payload, or null for a rule no scope desugared into.</summary>
    public ScopeData? Scope { get; }

    /// <summary>
    ///     The effective ratchet baseline path for this rule, or null when the rule is not ratcheted.
    /// </summary>
    /// <remarks>
    ///     Both Migrate rules and Quarantine containment rules grandfather their violations against a
    ///     baseline (GRAMMAR §7); a scope tripwire and an Enforce rule have none.
    /// </remarks>
    public string? BaselinePath => Migrate?.BaselinePath
                                   ?? (Scope is { Role: ScopeRole.Containment } containment ? containment.BaselinePath : null);
}
