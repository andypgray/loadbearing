namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A single source position: an absolute or solution-relative file path and a 1-based line
///     number. The agent-facing rendered form is <c>{FilePath}:{Line}</c> (pinned by tests) — the
///     <c>file:line</c> a violation message cites so an agent can jump straight to the offending
///     source.
/// </summary>
public sealed class SourceLocation
{
    internal SourceLocation(string filePath, int line)
    {
        FilePath = filePath;
        Line = line;
    }

    /// <summary>The file path — verbatim from the syntax tree (absolute under MSBuildWorkspace).</summary>
    public string FilePath { get; }

    /// <summary>The 1-based line number of the position.</summary>
    public int Line { get; }

    /// <summary>Renders the pinned agent-facing <c>file:line</c> form.</summary>
    public override string ToString()
    {
        return $"{FilePath}:{Line}";
    }
}