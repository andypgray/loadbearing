using System.Diagnostics;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Hooks;

/// <summary>
///     Pins the MCP registration <c>hooks/README.md</c> tells a clone to paste into <c>.mcp.json</c>, at the
///     line naming the interpreter: the POSIX form under a bare <c>sh</c>, the Windows form under the
///     shell's full path, and every file the arguments point at still where the registration says it is.
/// </summary>
/// <remarks>
///     <para>
///         That interpreter line is the one part of the recipe <see cref="McpLaunchScriptTests" /> cannot
///         see. It runs the launcher under a shell it located for itself, so it stays green whatever the
///         registration says — including a registration that could never start a shell on the reader's
///         machine at all.
///     </para>
///     <para>
///         The two Windows facts execute the reasoning behind the absolute spelling rather than restating
///         it. One starts the documented path with no help from <c>PATH</c>, which is the condition a stdio
///         client imposes: servers are spawned through <c>cmd.exe</c>, carrying the PATH a desktop process
///         inherits. The other holds the premise that makes the spelling necessary — a default Git for
///         Windows install puts <c>git.exe</c> on that PATH and leaves the shell off it — so the day git
///         ships a shell beside its other entry points, the README's reasoning is stale and something says
///         so.
///     </para>
///     <para>
///         Both skip with a named reason away from Windows and away from a default Git home: the claim is
///         about one machine shape, and a tick earned anywhere else would be a fiction.
///     </para>
/// </remarks>
public sealed class McpRegistrationTests
{
    /// <summary>Where a default Git for Windows install keeps the POSIX shell — off every PATH but Git Bash's own.</summary>
    private const string WindowsShell = @"C:\Program Files\Git\bin\sh.exe";

    /// <summary>The one directory that same install does contribute to the machine PATH: git's entry points.</summary>
    private const string GitPathEntry = @"C:\Program Files\Git\cmd";

    /// <summary>The interpreter line of the POSIX registration, exactly as a reader pastes it.</summary>
    private const string PosixCommandLine = "\"command\": \"sh\"";

    /// <summary>
    ///     The Windows interpreter line: same key, the shell named absolutely, every separator doubled
    ///     because a registration is JSON. Derived rather than typed, so the documented spelling cannot
    ///     drift from the path the Windows facts below actually start.
    /// </summary>
    private static readonly string WindowsCommandLine = $"\"command\": \"{WindowsShell.Replace(@"\", @"\\")}\"";

    /// <summary>Everything the registration's arguments name, repo-relative and in the order it names them.</summary>
    private static readonly string[] RegisteredPaths =
    [
        "hooks/mcp-launch.sh",
        "Zphil.LoadBearing.slnx",
        "arch/Zphil.LoadBearing.ArchSpec/Zphil.LoadBearing.ArchSpec.csproj"
    ];

    [Fact]
    public void Readme_RegistersThePosixShellAndNamesOnlyPathsThatExist()
    {
        string readme = ReadRepoFile("hooks/README.md");

        readme.ShouldContain(
            PosixCommandLine,
            "the registration a clone pastes has to name the interpreter that runs the launcher.");

        foreach (string path in RegisteredPaths)
        {
            readme.ShouldContain(
                $"\"{path}\"",
                $"the registration no longer names {path}, so what a reader pastes and what is held to "
                + "account here have come apart.");

            string absolute = RepoRoot.Absolute(path);
            File.Exists(absolute)
                .ShouldBeTrue(
                    $"the registration names {path}, which is not there. Pasted as written, it points the client "
                    + "at nothing and the server never reaches a handshake.");
        }
    }

    [Fact]
    public void Readme_NamesTheWindowsShellByWhereItLives()
    {
        string readme = ReadRepoFile("hooks/README.md");

        readme.ShouldContain(
            WindowsCommandLine,
            "this is the line that lets the registration start on a machine whose PATH comes from the "
            + "registry. A bare `sh` there fails every connect before the handshake with \"'sh' is not "
            + "recognized\".");
    }

    [Fact]
    public void LauncherHeader_NamesTheSameWindowsShell()
    {
        string launcher = ReadRepoFile("hooks/mcp-launch.sh");

        launcher.ShouldContain(
            WindowsCommandLine,
            "the launcher's own header carries the registration too, and the two documented surfaces have to "
            + "name one shell. A reader who follows either must arrive at the spelling that works.");
    }

    [Fact]
    public void TheDocumentedWindowsShell_StartsWithNoHelpFromPath()
    {
        Assert.SkipWhen(
            !OperatingSystem.IsWindows(),
            "the documented path is a Windows one, so there is nothing on this OS for it to start.");
        Assert.SkipWhen(
            !File.Exists(WindowsShell),
            $"{WindowsShell} is absent, so this machine cannot say whether the documented path starts a "
            + "shell. It runs wherever Git for Windows sits in its default home.");

        var startInfo = new ProcessStartInfo(WindowsShell);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("exit 42");

        // Started by its full path, which consults no PATH at all — the resolution a stdio client is denied.
        ChildProcess.ProcessResult result = ChildProcess.Run(startInfo, TimeSpan.FromMinutes(1));

        result.ExitCode.ShouldBe(
            42,
            $"{WindowsShell} did not run the script it was handed, so a registration naming it would not "
            + "start a server either.");
    }

    [Fact]
    public void GitsOwnPathEntry_StillCarriesNoShell()
    {
        Assert.SkipWhen(
            !OperatingSystem.IsWindows(),
            "the PATH entry under test belongs to a Windows install, so there is nothing on this OS to read.");
        Assert.SkipWhen(
            !Directory.Exists(GitPathEntry),
            $"{GitPathEntry} is absent, so this machine carries no default Git for Windows install to check.");

        string shell = Path.Combine(GitPathEntry, "sh.exe");

        File.Exists(shell)
            .ShouldBeFalse(
                $"{shell} exists, so a default install now puts a shell where the machine PATH reaches it and "
                + "the README's reason for naming the shell absolutely no longer holds. Revisit what it tells a "
                + "reader to paste.");
    }

    private static string ReadRepoFile(string repoRelativePath)
    {
        return File.ReadAllText(RepoRoot.Absolute(repoRelativePath));
    }
}
