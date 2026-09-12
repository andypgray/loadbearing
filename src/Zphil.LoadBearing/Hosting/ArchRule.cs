namespace Zphil.LoadBearing.Hosting;

/// <summary>
///     One rule of the finished model: its ID, its posture, why it exists, the sentence it reads as, and
///     whatever its posture carries besides. A scope contributes rules here too — a quarantine two, a
///     caution one — each of them carrying <see cref="Scope" />, so anything reading the model sees one
///     kind of rule whatever declared it.
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
        ScopeData? scope,
        string? citation = null)
    {
        Id = id;
        Posture = posture;
        Because = because;
        Fix = fix;
        Sentence = sentence;
        Constraint = constraint;
        Migrate = migrate;
        Scope = scope;
        Citation = citation;
    }

    /// <summary>
    ///     Gets the rule's ID as the spec declared it: the name printed with every violation, the handle the
    ///     CLI's <c>explain</c> takes, and the key its baseline file is named from. A scope's own rules are
    ///     <c>{scope-id}/containment</c> and <c>{scope-id}/tripwire</c>.
    /// </summary>
    public string Id { get; }

    /// <summary>
    ///     Gets how the rule is enforced: whether a violation fails the check, is grandfathered as debt being
    ///     paid down, or only warns.
    /// </summary>
    public Posture Posture { get; }

    /// <summary>
    ///     Gets why the rule exists, in the spec's own words. Rendered into the generated agent context and
    ///     echoed with every violation.
    /// </summary>
    public string Because { get; }

    /// <summary>
    ///     Gets what to do instead, in the spec's own words, or null when the spec gave none. A quarantine's
    ///     containment rule carries one the scope generated, naming the sanctioned surface to go through.
    /// </summary>
    public string? Fix { get; }

    /// <summary>
    ///     Gets the page the rule's reasoning rests on, as an absolute http(s) URL, or null when the spec
    ///     named none. Only a rule declared with <c>Enforce</c> or <c>Migrate</c> can carry one; a rule a
    ///     scope contributed never does.
    /// </summary>
    public string? Citation { get; }

    /// <summary>
    ///     Gets the rule as one English sentence — what the check report, <c>explain</c>, the generated agent
    ///     context and SARIF output all print. Empty for a scope's tripwire rule, which asserts nothing about
    ///     the code: it warns about edits inside the scope rather than judging them.
    /// </summary>
    public string Sentence { get; }

    /// <summary>
    ///     Gets what the check evaluates: the constraint an <c>Enforce</c> rule was given, the target
    ///     constraint of a <c>Migrate</c>, or the containment condition a quarantine generated. Null for a
    ///     scope's tripwire rule, which has nothing to evaluate.
    /// </summary>
    public Constraint? Constraint { get; }

    /// <summary>
    ///     Gets what a <c>Migrate</c> rule carries besides — the description of the pattern the code still
    ///     follows, the baseline file, and the migration policy. Null for every other posture.
    /// </summary>
    public MigrateData? Migrate { get; }

    /// <summary>
    ///     Gets what a rule contributed by a scope carries besides — which half of the scope it is, the
    ///     sanctioned surface, the dragons prose. Null for a rule the spec declared directly.
    /// </summary>
    public ScopeData? Scope { get; }

    /// <summary>
    ///     Gets the file recording this rule's grandfathered violations, or null when the rule grandfathers
    ///     nothing. A <c>Migrate</c> rule and a quarantine's containment rule each have one and it is always
    ///     filled in — the path the spec gave, or the conventional default derived from the rule ID; an
    ///     <c>Enforce</c> rule and a scope's tripwire have none. A relative path resolves against the
    ///     solution directory.
    /// </summary>
    public string? BaselinePath => Migrate?.BaselinePath
                                   ?? (Scope is { Role: ScopeRole.Containment } containment ? containment.BaselinePath : null);

    /// <summary>
    ///     Gets whether this rule grandfathers existing violations rather than failing on them — true for a
    ///     <c>Migrate</c> rule and for a quarantine's containment rule, false for <c>Enforce</c> and for a
    ///     scope's tripwire.
    /// </summary>
    // Keyed on the payload rather than the posture: having a baseline to measure against IS what ratcheting
    // means, and containment ratchets without the posture saying so. Every reader asking the question asks
    // it through here, so a future posture that also ratchets is one edit rather than a hunt. A reader that
    // needs the file itself reads BaselinePath instead — that is a different question.
    public bool IsRatcheted => BaselinePath is not null;
}
