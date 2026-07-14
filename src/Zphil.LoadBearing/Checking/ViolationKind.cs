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

    /// <summary>The subject selection matched no types, so the rule fails by default (GRAMMAR §4.1). Carries <c>Detail</c>.</summary>
    EmptySubject,

    /// <summary>
    ///     Evaluation could not proceed (unrepresentable type, closed-generic noun, throwing predicate). Carries
    ///     <c>Detail</c>.
    /// </summary>
    RuleError
}