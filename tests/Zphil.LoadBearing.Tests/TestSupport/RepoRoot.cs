namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Locates this repository's root by walking up from <see cref="AppContext.BaseDirectory" /> to the
///     directory that holds the solution file — so the dogfood self-spec tests can point the CLI at the
///     real solution and read the committed <c>AGENTS.md</c> regardless of the test host's working
///     directory.
/// </summary>
internal static class RepoRoot
{
    private const string SolutionFileName = "Zphil.LoadBearing.slnx";

    /// <summary>The repository root directory.</summary>
    public static string Directory { get; } = Find();

    /// <summary>The absolute path to the solution file.</summary>
    public static string Solution => Path.Combine(Directory, SolutionFileName);

    /// <summary>The absolute path to the dogfood arch-spec csproj (the <c>--spec</c> the self-check passes).</summary>
    public static string ArchSpecCsproj =>
        Path.Combine(Directory, "arch", "Zphil.LoadBearing.ArchSpec", "Zphil.LoadBearing.ArchSpec.csproj");

    /// <summary>The absolute path to the committed root <c>AGENTS.md</c>.</summary>
    public static string AgentsMd => Path.Combine(Directory, "AGENTS.md");

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate {SolutionFileName} walking up from {AppContext.BaseDirectory}.");
    }
}
