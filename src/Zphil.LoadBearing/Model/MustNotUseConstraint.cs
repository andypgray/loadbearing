using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustNotUse(member, …)</c> → "must not use {list}" (GRAMMAR §5.3, §4.5).</summary>
internal sealed class MustNotUseConstraint : Constraint
{
    internal MustNotUseConstraint(Selection subject, IReadOnlyList<Member> members)
        : base(subject)
    {
        Members = members;
    }

    /// <summary>The forbidden member-access targets.</summary>
    internal IReadOnlyList<Member> Members { get; }

    internal override IReadOnlyList<Member> MemberOperands => Members;

    internal override string VerbPhrase => "must not use " + SentenceRenderer.MemberList(Members);
}