namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Locates the shells this repository's committed recipes run under, for the suites that execute those
///     recipes as real child processes.
/// </summary>
/// <remarks>
///     <c>sh</c> is the gate proper — every CI OS has a POSIX shell, Git for Windows supplying it on Windows,
///     where it ships without necessarily landing on the machine <c>PATH</c>, hence the two fallback homes.
///     <c>pwsh</c> is present on some machines and not others (not on the maintainer's), so a suite that wants
///     it skips with a named reason rather than failing.
/// </remarks>
internal static class ShellInterpreter
{
    /// <summary>The POSIX shell: the interpreter every committed <c>.sh</c> recipe is run under.</summary>
    internal const string Sh = "sh";

    /// <summary>PowerShell 7, the interpreter the <c>.ps1</c> wrapper is run under.</summary>
    internal const string Pwsh = "pwsh";

    /// <summary>
    ///     The path to <paramref name="command" />, or null where it is not installed. The caller decides
    ///     whether that is a skip or a failure.
    /// </summary>
    internal static string? Locate(string command)
    {
        return SearchPath(command) ?? GitForWindowsShell(command);
    }

    private static string? SearchPath(string command)
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? [command + ".exe", command + ".cmd", command + ".bat"]
            : [command];

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;

            try
            {
                foreach (string candidate in candidates)
                {
                    string full = Path.Combine(directory.Trim('"'), candidate);
                    if (File.Exists(full)) return full;
                }
            }
            catch (ArgumentException)
            {
                // A PATH entry with invalid path characters — skip it, as the OS loader does.
            }
        }

        return null;
    }

    private static string? GitForWindowsShell(string command)
    {
        if (command != Sh || !OperatingSystem.IsWindows()) return null;

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        [
            Path.Combine(programFiles, "Git", "usr", "bin", "sh.exe"),
            Path.Combine(programFiles, "Git", "bin", "sh.exe")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }
}