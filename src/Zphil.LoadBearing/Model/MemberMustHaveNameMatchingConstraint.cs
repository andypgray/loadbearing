using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.Methods.MustHaveNameMatching("*Async")</c> → "must have a name matching `*Async`" (GRAMMAR §5.7).</summary>
internal sealed class MemberMustHaveNameMatchingConstraint(MemberSelection subject, string glob) : MemberConstraint(subject)
{
    /// <summary>The member-name glob.</summary>
    internal string Glob { get; } = glob;

    internal override string VerbPhrase => "must have a name matching " + ProseFormat.Backtick(Glob);
}