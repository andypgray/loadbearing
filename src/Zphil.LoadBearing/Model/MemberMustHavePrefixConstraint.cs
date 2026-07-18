using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.Methods.MustHavePrefix("I")</c> → "must be named `I*`" (GRAMMAR §5.7; reuses the §5.3 fragment).</summary>
internal sealed class MemberMustHavePrefixConstraint(MemberSelection subject, string prefix) : MemberConstraint(subject)
{
    /// <summary>The required name prefix.</summary>
    internal string Prefix { get; } = prefix;

    internal override string VerbPhrase => "must be named " + ProseFormat.Backtick(Prefix + "*");
}