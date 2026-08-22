using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustResideInProject("MyApp.Web")</c> → "must reside in project `MyApp.Web`" (GRAMMAR §5.3).</summary>
internal sealed class MustResideInProjectConstraint(Selection subject, string projectName) : Constraint(subject)
{
    /// <summary>The project name the subject must be declared by — any declarer of it satisfies the verb (GRAMMAR §4.1).</summary>
    internal string ProjectName { get; } = projectName;

    internal override string VerbPhrase => "must reside in project " + ProseFormat.Backtick(ProjectName);
}
