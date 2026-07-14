using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustResideInNamespace("MyApp.Web.*")</c> → "must reside in `MyApp.Web.*`" (GRAMMAR §5.3).</summary>
internal sealed class MustResideInNamespaceConstraint(Selection subject, string glob) : Constraint(subject)
{
    /// <summary>The namespace glob the subject must reside in.</summary>
    internal string Glob { get; } = glob;

    internal override string VerbPhrase => "must reside in " + ProseFormat.Backtick(Glob);
}