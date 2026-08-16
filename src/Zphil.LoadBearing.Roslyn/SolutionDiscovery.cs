namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Resolves the target solution file: an explicit path, then the
///     <see cref="LoadBearingEnvVars.SolutionPath" /> environment variable, then a walk up from the
///     working directory matching <c>.sln</c>/<c>.slnf</c>/<c>.slnx</c>. The first ancestor holding
///     exactly one solution wins, where a <c>.slnf</c> counts only when no full solution stands beside it.
/// </summary>
/// <remarks>
///     When the walk-up finds nothing it refuses; it never widens the search and picks. Both refusals
///     (<see cref="AmbiguousMessage" />, <see cref="NotFoundMessage" />) name the solution argument before
///     the environment variable, and the "nothing anywhere" one names any solution one level down — the two
///     shapes a repository actually arrives in, measured against real ones: a solution under <c>src\</c>, or
///     several at the root. Naming the file the reader needs is what turns a refusal into one copy-paste.
/// </remarks>
public static class SolutionDiscovery
{
    /// <summary>How many near misses the "nothing anywhere" refusal lists before the "and N more" tail.</summary>
    private const int MaxNearMisses = 5;

    // Build output and tooling directories: a solution under one of these is an artefact or a checkout of
    // something else, never the repository's own, and naming it would send the reader somewhere wrong.
    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git", ".vs", "node_modules", "packages"];

    // Both refusals end here. The argument comes first because it is the better answer on both surfaces the
    // walk-up serves: at the CLI it is the normal spelling, and for an MCP client the args array in the
    // server registration is where the answer has to live — the environment variable would have to be set
    // for whatever process launches the client.
    private static string FixLine =>
        "Pass the solution as the argument (loadbearing <command> <solution>, or in your MCP client "
        + $"config's args), or set {LoadBearingEnvVars.SolutionPath} to it.";

    /// <summary>
    ///     Discovers the solution file to load.
    /// </summary>
    /// <param name="explicitPath">An explicit solution path; when set it must exist.</param>
    /// <param name="workingDirectory">The directory to start the walk-up from; defaults to the CWD.</param>
    /// <returns>The absolute path to the resolved solution file.</returns>
    /// <exception cref="FileNotFoundException">An explicit or env-var path was given but no file exists there.</exception>
    /// <exception cref="InvalidOperationException">No single solution was found, or a directory held several.</exception>
    public static string DiscoverSolution(string? explicitPath = null, string? workingDirectory = null)
    {
        // Every returned path is canonicalized (symlinks resolved) so the workspace's document paths
        // agree with git's canonical toplevel — the tripwire prefix match otherwise misses on a
        // symlinked root (macOS /var → /private/var, a symlinked home, a junction). See PathCanonicalizer.
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            string resolved = PathCanonicalizer.Resolve(explicitPath);
            if (!File.Exists(resolved)) throw new FileNotFoundException($"Solution file not found: {resolved}", resolved);

            return resolved;
        }

        string? envPath = Environment.GetEnvironmentVariable(LoadBearingEnvVars.SolutionPath);
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            string resolved = PathCanonicalizer.Resolve(envPath);
            if (!File.Exists(resolved))
                throw new FileNotFoundException(
                    $"{LoadBearingEnvVars.SolutionPath} points to a file that does not exist: {resolved}", resolved);

            return resolved;
        }

        string cwd = workingDirectory ?? Directory.GetCurrentDirectory();
        (string dir, string[] files)? firstAmbiguous = null;

        string? current = cwd;
        while (current is not null)
        {
            string[] slnFiles = FindSlnFiles(current);
            if (slnFiles.Length == 1) return PathCanonicalizer.Resolve(slnFiles[0]);

            if (slnFiles.Length > 1 && firstAmbiguous is null) firstAmbiguous = (current, slnFiles);

            current = Directory.GetParent(current)?.FullName;
        }

        if (firstAmbiguous is { } ambiguous)
            throw new InvalidOperationException(AmbiguousMessage(ambiguous.dir, ambiguous.files));

        throw new InvalidOperationException(NotFoundMessage(cwd, NearMisses(cwd)));
    }

    /// <summary>
    ///     The refusal when the first directory holding any solution holds several. Pure, and internal so the
    ///     text pins without a filesystem — the split <see cref="MsBuild.MsBuildBootstrap.DescribeSelection" />
    ///     makes for the same reason.
    /// </summary>
    /// <param name="directory">The ancestor that held them.</param>
    /// <param name="candidates">The solution files found there; rendered by file name.</param>
    internal static string AmbiguousMessage(string directory, IReadOnlyList<string> candidates)
    {
        return $"Multiple solution files found in '{directory}':\n"
               + $"  {string.Join("\n  ", candidates.Select(Path.GetFileName))}\n"
               + FixLine;
    }

    /// <summary>
    ///     The refusal when the whole walk-up found nothing, naming any solution one level below the start
    ///     directory (<paramref name="nearMisses" />) so a repository whose solution sits under <c>src\</c> is
    ///     one copy-paste from working rather than a dead end. The list is capped, with an "and N more" tail.
    /// </summary>
    /// <param name="directory">The directory the walk-up started from.</param>
    /// <param name="nearMisses">
    ///     Absolute paths from <see cref="NearMisses" />; rendered relative to
    ///     <paramref name="directory" />.
    /// </param>
    internal static string NotFoundMessage(string directory, IReadOnlyList<string> nearMisses)
    {
        var lines = new List<string> { $"No .sln, .slnf or .slnx file found in '{directory}' or any parent directory." };

        if (nearMisses.Count > 0)
        {
            lines.Add("Solution files one level down:");
            lines.AddRange(nearMisses.Take(MaxNearMisses).Select(path => "  " + Path.GetRelativePath(directory, path)));
            if (nearMisses.Count > MaxNearMisses) lines.Add($"  ... and {nearMisses.Count - MaxNearMisses} more");
        }

        lines.Add(FixLine);
        return string.Join("\n", lines);
    }

    /// <summary>
    ///     Solutions in the immediate subdirectories of <paramref name="startDirectory" />, ordered, with the
    ///     build-output and tooling directories skipped. One bounded scan, run only when the walk-up has
    ///     already failed: discovery reports what it saw and still refuses — it never widens the search into
    ///     a guess. An unreadable directory yields nothing rather than replacing the refusal with an I/O error.
    /// </summary>
    internal static IReadOnlyList<string> NearMisses(string startDirectory)
    {
        try
        {
            return Directory.EnumerateDirectories(startDirectory)
                .Where(directory => !SkippedDirectories.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase))
                .OrderBy(directory => directory, StringComparer.Ordinal)
                .SelectMany(FindSlnFiles)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    ///     The solution files in one directory, ordered, with filters demoted: a <c>.slnf</c> standing beside
    ///     a <c>.sln</c> or <c>.slnx</c> drops out of the candidate set entirely. A filter is a view of a
    ///     solution, so the two together is a normal layout rather than a question — and a refusal naming a
    ///     filter would invite a fix (delete it, pass it explicitly) that changes nothing. Where no full
    ///     solution stands beside them, filters resolve and refuse each other exactly as before.
    /// </summary>
    private static string[] FindSlnFiles(string directory)
    {
        string[] candidates = Directory.EnumerateFiles(directory)
            .Where(IsSolutionFile)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] fullSolutions = candidates
            .Where(path => !IsFilter(path))
            .ToArray();

        return fullSolutions.Length > 0 ? fullSolutions : candidates;
    }

    private static bool IsSolutionFile(string path)
    {
        return path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
               || IsFilter(path)
               || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFilter(string path)
    {
        return path.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase);
    }
}
