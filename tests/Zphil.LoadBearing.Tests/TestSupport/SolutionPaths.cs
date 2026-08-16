using Shouldly;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The solution-file facts the discovery and binding suites need on disk: where a solution's directory
///     is, an empty solution file for the walk-up to land on, and the precondition every "the walk-up finds
///     nothing" row rests on.
/// </summary>
internal static class SolutionPaths
{
    /// <summary>
    ///     The directory holding <paramref name="solutionPath" />, fully resolved — the working directory a
    ///     run anchored at that solution is given.
    /// </summary>
    internal static string SolutionDirectoryOf(string solutionPath)
    {
        return Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
    }

    /// <summary>An empty solution file named <paramref name="name" /> under <paramref name="directory" />.</summary>
    /// <remarks>
    ///     Empty on purpose: discovery decides on a candidate's extension and existence, never on its contents.
    /// </remarks>
    internal static string CreateSln(string directory, string name)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, "");
        return path;
    }

    /// <summary>
    ///     Asserts that neither <paramref name="directory" /> nor any of its ancestors holds a solution file.
    /// </summary>
    /// <remarks>
    ///     Discovery walks parents to the drive root, so a stray solution in <em>any</em> ancestor of a temp
    ///     root would resolve one and quietly void the row. Fail loudly on a polluted environment instead —
    ///     including for the ambiguous shape, whose walk-up does not stop at the first ambiguous directory but
    ///     keeps climbing for a single one. <paramref name="consequence" /> completes the sentence
    ///     <c>Stray solution file under ancestor '&lt;dir&gt;' …</c>, so each caller says what one would cost it.
    /// </remarks>
    internal static void ShouldHaveNoSolutionInAnyAncestor(string directory, string consequence)
    {
        for (DirectoryInfo? dir = new(directory); dir is not null; dir = dir.Parent)
        {
            string[] solutionFiles;
            try
            {
                solutionFiles = Directory.EnumerateFiles(dir.FullName, "*.sln")
                    .Concat(Directory.EnumerateFiles(dir.FullName, "*.slnf"))
                    .Concat(Directory.EnumerateFiles(dir.FullName, "*.slnx"))
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            solutionFiles.ShouldBeEmpty($"Stray solution file under ancestor '{dir.FullName}' {consequence}");
        }
    }
}
