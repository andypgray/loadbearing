namespace Zphil.LoadBearing;

/// <summary>
///     One reified rule (DESIGN.md §6): a stable ID, a posture, rationale, an optional fix, the
///     deterministic law <see cref="Sentence" />, and the posture-specific payload. Freeze scopes
///     desugar into <see cref="Posture.Freeze" /> rules carrying <see cref="Freeze" /> (GRAMMAR §7).
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
        FreezeData? freeze)
    {
        Id = id;
        Posture = posture;
        Because = because;
        Fix = fix;
        Sentence = sentence;
        Constraint = constraint;
        Migrate = migrate;
        Freeze = freeze;
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
    ///     The rendered law sentence (GRAMMAR §6). Empty for a Freeze tripwire, which carries no
    ///     closed-vocabulary constraint — it is a diff-aware touch check, not a law (GRAMMAR §7).
    /// </summary>
    public string Sentence { get; }

    /// <summary>
    ///     The checkable constraint — the <c>Enforce</c> constraint, the Migrate <c>to</c> target,
    ///     or the Freeze containment predicate. Null for a Freeze tripwire.
    /// </summary>
    public Constraint? Constraint { get; }

    /// <summary>Migrate-specific payload, or null for non-Migrate rules.</summary>
    public MigrateData? Migrate { get; }

    /// <summary>Freeze-specific payload, or null for non-Freeze rules.</summary>
    public FreezeData? Freeze { get; }

    /// <summary>
    ///     The effective ratchet baseline path for this rule, or null when the rule is not ratcheted.
    ///     Both Migrate rules and Freeze containment rules grandfather their violations against a
    ///     baseline (DESIGN.md §5, GRAMMAR §7); a Freeze tripwire and an Enforce rule have none. The
    ///     one accessor every renderer/store consults to ask "is this a ratcheted rule".
    /// </summary>
    public string? BaselinePath => Migrate?.BaselinePath
                                   ?? (Freeze is { Role: FreezeRole.Containment } freeze ? freeze.BaselinePath : null);
}