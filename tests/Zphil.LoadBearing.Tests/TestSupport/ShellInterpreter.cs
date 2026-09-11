using Xunit;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Locates the shells this repository's committed recipes run under, for the suites that execute those
///     recipes as real child processes.
/// </summary>
/// <remarks>
///     <c>sh</c> is the gate proper — every CI OS has a POSIX shell, Git for Windows supplying it on Windows,
///     where it ships without necessarily landing on the machine <c>PATH</c>, hence the two fallback homes.
///     <c>pwsh</c> is present on some machines and not others, so a suite that wants it skips with a
///     named reason rather than failing.
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

    /// <summary>
    ///     The path to <paramref name="command" />, skipping the test with a named reason where it is not
    ///     installed — the shape every suite that shells out wants.
    /// </summary>
    internal static string Require(string command)
    {
        string path = Locate(command) ?? string.Empty;
        Assert.SkipWhen(
            path.Length == 0,
            $"'{command}' is not available on this machine, so this arm cannot run here. It runs wherever "
            + "the interpreter is installed, which for sh is every CI OS.");

        return path;
    }

    /// <summary>
    ///     <paramref name="path" /> with forward slashes throughout: a POSIX shell takes <c>C:/...</c> as a
    ///     path, while a backslash inside it is an escape character rather than a separator.
    /// </summary>
    internal static string Posix(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    ///     Writes an executable stub named <paramref name="name" /> into <paramref name="directory" />, in
    ///     both shapes a shell can reach: an extensionless shebang script carrying
    ///     <paramref name="shScript" /> (what a POSIX shell resolves, on every OS), and — where
    ///     <paramref name="cmdScript" /> is given — a <c>.cmd</c> twin (what PowerShell resolves on Windows,
    ///     where an extensionless file is not an executable).
    /// </summary>
    internal static void WriteExecutableStub(
        string directory, string name, string shScript, string? cmdScript = null)
    {
        string posix = Path.Combine(directory, name);
        File.WriteAllText(posix, shScript);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                posix,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        if (cmdScript is not null) File.WriteAllText(Path.Combine(directory, name + ".cmd"), cmdScript);
    }

    /// <summary>
    ///     A <c>PATH</c> with <paramref name="directory" /> in front of this process's — how a stub is put
    ///     where a child resolves the real command from.
    /// </summary>
    internal static string PrependedPath(string directory)
    {
        return directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
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
