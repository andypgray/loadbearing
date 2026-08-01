using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Methods.MustNotBeAttributedWith(typeof(ObsoleteAttribute), …)</c> → "must not be attributed
///     with `[Obsolete]`" (GRAMMAR §5.7) — the type-side verb phrase reused verbatim (verb position is
///     unambiguous, so the member wording has nothing to disambiguate). None-of over the anchor list: a
///     subject member violates iff it carries ANY anchor attribute. Anchors are stored as raw
///     <see cref="AttributeAnchor" />s on the node, never selection operands (GRAMMAR §10); one list mixes
///     no forms, because the overloads are homogeneous — all <c>typeof</c> or all definition names.
/// </summary>
internal sealed class MemberMustNotBeAttributedWithConstraint(MemberSelection subject, IReadOnlyList<AttributeAnchor> anchors)
    : MemberConstraint(subject)
{
    /// <summary>The attributes the subject members must not carry (none-of).</summary>
    internal IReadOnlyList<AttributeAnchor> Anchors { get; } = anchors;

    internal override string VerbPhrase => "must not be attributed with " + ProseFormat.AttributeList(Anchors);
}