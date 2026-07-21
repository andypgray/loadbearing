using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustHaveNameMatching("*Repo*")</c> → "must have a name matching `*Repo*`" (GRAMMAR §5.3).</summary>
internal sealed class MustHaveNameMatchingConstraint(Selection subject, string glob) : Constraint(subject)
{
    /// <summary>The type-name glob (a different matcher from <see cref="NamespacePattern" />).</summary>
    internal string Glob { get; } = glob;

    internal override string VerbPhrase => "must have a name matching " + ProseFormat.Backtick(Glob);
}