using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustNotUse(member, …)</c> → "must not use {list}" (GRAMMAR §5.3, §4.5).</summary>
internal sealed class MustNotUseConstraint(Selection subject, IReadOnlyList<Member> members) : Constraint(subject)
{
    /// <summary>The forbidden member-access targets.</summary>
    internal IReadOnlyList<Member> Members { get; } = members;

    internal override IReadOnlyList<Member> MemberOperands => Members;

    internal override string VerbPhrase => "must not use " + SentenceRenderer.MemberList(Members);
}
