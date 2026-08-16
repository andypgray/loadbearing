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

    /// <summary>The absolute path to the dogfood arch-spec's source file (the verb ledger's home).</summary>
    public static string ArchSpecSource =>
        Path.Combine(Directory, "arch", "Zphil.LoadBearing.ArchSpec", "LoadBearingArchSpec.cs");

    /// <summary>The absolute path to the committed root <c>AGENTS.md</c>.</summary>
    public static string AgentsMd => Path.Combine(Directory, "AGENTS.md");

    /// <summary>The absolute path to the committed root <c>ARCHITECTURE.md</c> (the rendered diagram).</summary>
    public static string ArchitectureMd => Path.Combine(Directory, "ARCHITECTURE.md");

    /// <summary>
    ///     The absolute path to the solution-level ReSharper settings file — the solution path plus the
    ///     <c>.DotSettings</c> suffix, which is how ReSharper itself derives it.
    /// </summary>
    public static string SolutionDotSettings => Solution + ".DotSettings";

    /// <summary>
    ///     The absolute path to the test project's ReSharper settings layer — a project layer's file is
    ///     the csproj path plus the <c>.DotSettings</c> suffix, and jb mounts it above the solution layer.
    /// </summary>
    public static string TestProjectDotSettings =>
        Absolute("tests/Zphil.LoadBearing.Tests/Zphil.LoadBearing.Tests.csproj.DotSettings");

    /// <summary>The absolute native path for a forward-slash repo-relative one.</summary>
    public static string Absolute(string repoRelativePath)
    {
        return Path.Combine([Directory, .. repoRelativePath.Split('/')]);
    }

    /// <summary>The whole text of a committed file, named by its forward-slash repo-relative path.</summary>
    public static string ReadText(string repoRelativePath)
    {
        return File.ReadAllText(Absolute(repoRelativePath));
    }

    /// <summary>The lines of a committed file, named by its forward-slash repo-relative path.</summary>
    public static string[] ReadLines(string repoRelativePath)
    {
        return File.ReadAllLines(Absolute(repoRelativePath));
    }

    /// <summary>The forward-slash repo-relative path for an absolute one — how the repo spells its own files.</summary>
    public static string Relative(string absolutePath)
    {
        return Path.GetRelativePath(Directory, absolutePath)
            .Replace('\\', '/');
    }

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
