using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustHavePrefix("I")</c> → "must be named `I*`" (GRAMMAR §5.3).</summary>
internal sealed class MustHavePrefixConstraint(Selection subject, string prefix) : Constraint(subject)
{
    /// <summary>The required name prefix.</summary>
    internal string Prefix { get; } = prefix;

    internal override string VerbPhrase => "must be named " + ProseFormat.Backtick(Prefix + "*");
}