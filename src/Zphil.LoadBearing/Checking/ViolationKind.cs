namespace Zphil.LoadBearing.Checking;

/// <summary>What a <see cref="Violation" /> is about — which of its nullable slots are populated (GRAMMAR §4.3).</summary>
public enum ViolationKind
{
    /// <summary>
    ///     A forbidden or non-permitted reference edge: <c>Source</c> references <c>Target</c>, with
    ///     the edge's reference <c>Sites</c>. One per (rule, source, target).
    /// </summary>
    Reference,

    /// <summary>
    ///     A subject failing a shape/naming/inheritance/attribute/escape verb: <c>Subject</c> with its
    ///     declaration <c>Sites</c>. One per (rule, subject).
    /// </summary>
    Shape,

    /// <summary>
    ///     A banned member access (GRAMMAR §4.5): <c>Source</c> uses <c>Member</c>, with the member-use
    ///     edge's <c>Sites</c>. One per (rule, source, member) — per-overload edges yield per-overload
    ///     violations (GRAMMAR §4.3).
    /// </summary>
    MemberUse,

    /// <summary>
    ///     A declared member failing a member shape/naming/escape verb (GRAMMAR §4.6): <c>SubjectMember</c>
    ///     with its declaration <c>Sites</c>. One per (rule, member) — keyed by the member's own DocId, so a
    ///     renamed or newly-added member is a NEW red.
    /// </summary>
    MemberShape,

    /// <summary>
    ///     A forbidden construction edge (GRAMMAR §4.5): <c>Source</c> constructs <c>Target</c> (the constructed
    ///     type) with an object-creation expression, carrying every <c>new</c> <c>Sites</c>. One per (rule,
    ///     source, constructed) — overload-indifferent, since all constructor overloads of the constructed type
    ///     collapse to one identity and the sites are evidence, not identity (GRAMMAR §4.3).
    /// </summary>
    Construction,

    /// <summary>The subject selection matched no types, so the rule fails by default (GRAMMAR §4.1). Carries <c>Detail</c>.</summary>
    EmptySubject,

    /// <summary>
    ///     Evaluation could not proceed (unrepresentable type, closed-generic noun, throwing predicate). Carries
    ///     <c>Detail</c>.
    /// </summary>
    RuleError
}