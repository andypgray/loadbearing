namespace Zphil.LoadBearing;

/// <summary>
///     One reified rule: a stable ID, a posture, rationale, an optional fix, the
///     deterministic law <see cref="Sentence" />, and the posture-specific payload. Quarantine scopes
///     desugar into <see cref="Posture.Quarantine" /> rules carrying <see cref="Quarantine" /> (GRAMMAR §7).
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
        QuarantineData? quarantine)
    {
        Id = id;
        Posture = posture;
        Because = because;
        Fix = fix;
        Sentence = sentence;
        Constraint = constraint;
        Migrate = migrate;
        Quarantine = quarantine;
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
    ///     The rendered law sentence (GRAMMAR §6). Empty for a Quarantine tripwire, which carries no
    ///     closed-vocabulary constraint — it is a diff-aware touch check, not a law (GRAMMAR §7).
    /// </summary>
    public string Sentence { get; }

    /// <summary>
    ///     The checkable constraint — the <c>Enforce</c> constraint, the Migrate <c>to</c> target,
    ///     or the Quarantine containment predicate. Null for a Quarantine tripwire.
    /// </summary>
    public Constraint? Constraint { get; }

    /// <summary>Migrate-specific payload, or null for non-Migrate rules.</summary>
    public MigrateData? Migrate { get; }

    /// <summary>Quarantine-specific payload, or null for non-Quarantine rules.</summary>
    public QuarantineData? Quarantine { get; }

    /// <summary>
    ///     The effective ratchet baseline path for this rule, or null when the rule is not ratcheted.
    /// </summary>
    /// <remarks>
    ///     Both Migrate rules and Quarantine containment rules grandfather their violations against a
    ///     baseline (GRAMMAR §7); a Quarantine tripwire and an Enforce rule have none.
    /// </remarks>
    public string? BaselinePath => Migrate?.BaselinePath
                                   ?? (Quarantine is { Role: QuarantineRole.Containment } quarantine ? quarantine.BaselinePath : null);
}
