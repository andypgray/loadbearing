using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Methods.MustBeAttributedWith(typeof(McpServerToolAttribute))</c> → "must be attributed with
///     `[McpServerTool]`" (GRAMMAR §5.7) — the type-side verb phrase reused verbatim. Verb position is
///     unambiguous (nothing else can occupy it), so there is nothing to disambiguate and no reason for the
///     member wording to diverge from <see cref="MustBeAttributedWithConstraint" />; only the
///     <em>adjective</em> had to move (<see cref="MemberAttributedWithAdjective" />). The anchor is a
///     <see cref="TypeAnchor" />, so the string form renders the identical verb phrase.
/// </summary>
internal sealed class MemberMustBeAttributedWithConstraint(MemberSelection subject, TypeAnchor anchor) : MemberConstraint(subject)
{
    /// <summary>The attribute the subject members must carry, by <c>typeof</c> or by definition name.</summary>
    internal TypeAnchor Anchor { get; } = anchor;

    internal override string VerbPhrase => "must be attributed with " + ProseFormat.Backtick(ProseFormat.AttributeName(Anchor));
}
