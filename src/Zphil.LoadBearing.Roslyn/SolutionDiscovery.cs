namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Resolves the target solution file: an explicit path, then the
///     <see cref="LoadBearingEnvVars.SolutionPath" /> environment variable, then a walk up from the
///     working directory matching <c>.sln</c>/<c>.slnf</c>/<c>.slnx</c>. The first ancestor holding
///     exactly one solution wins.
/// </summary>
public static class SolutionDiscovery
{
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
            throw new InvalidOperationException(
                $"Multiple solution files found in '{ambiguous.dir}':\n" +
                $"  {string.Join("\n  ", ambiguous.files.Select(Path.GetFileName))}\n" +
                $"Set {LoadBearingEnvVars.SolutionPath} to the desired solution file.");

        throw new InvalidOperationException(
            $"No .sln, .slnf or .slnx file found in '{cwd}' or any parent directory.\n" +
            $"Set the {LoadBearingEnvVars.SolutionPath} environment variable to the solution file.");
    }

    private static string[] FindSlnFiles(string directory)
    {
        return Directory.EnumerateFiles(directory)
            .Where(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}