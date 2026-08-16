using System.Diagnostics;
using System.Text.Json;
using Zphil.LoadBearing.Roslyn.Hosting;

namespace Zphil.LoadBearing.Roslyn.MsBuild;

/// <summary>
///     Discovers installed Visual Studio instances via the standard <c>vswhere.exe</c> tool.
/// </summary>
/// <remarks>
///     Used in place of <c>MSBuildLocator.QueryVisualStudioInstances()</c> because on .NET 5+ that
///     API only returns DotNetSdk and DevConsole entries — never VS Setup instances. LoadBearing
///     runs on .NET 10, so it would otherwise miss every installed VS. <c>vswhere.exe</c> ships with
///     the VS Installer at a fixed path and reliably enumerates all VS installs (stable + preview)
///     including ones not yet picked up by MSBuildLocator.
/// </remarks>
internal static class VsWhereLocator
{
    // A local enumeration that normally returns in well under a second, so 10s only trips on a wedge —
    // and a trip costs nothing but the RegisterDefaults() fallback this probe already degrades to.
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

    private static readonly string VsWherePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Microsoft Visual Studio",
        "Installer",
        "vswhere.exe");

    /// <summary>
    ///     Runs vswhere and returns all discovered VS instances.
    /// </summary>
    /// <returns>
    ///     The discovered instances, or an empty list when vswhere is unavailable (non-Windows
    ///     hosts, missing install) or fails. Callers treat empty as a signal to fall back to
    ///     <see cref="Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults" />.
    /// </returns>
    internal static IReadOnlyList<VsInstance> Query()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(VsWherePath)) return [];

        try
        {
            string json = RunVsWhere();
            return ParseInstances(json);
        }
        catch (Exception)
        {
            // vswhere failure is non-fatal: callers fall back to MSBuildLocator.RegisterDefaults().
            return [];
        }
    }

    /// <summary>
    ///     Parses vswhere's JSON output into <see cref="VsInstance" /> records.
    /// </summary>
    /// <param name="json">JSON array as emitted by <c>vswhere -format json</c>.</param>
    /// <returns>
    ///     One record per array element with a valid <c>installationPath</c> and parseable
    ///     <c>installationVersion</c>. Empty when <paramref name="json" /> is blank, not an array,
    ///     or contains no usable entries.
    /// </returns>
    /// <remarks>
    ///     Exposed (rather than kept private) so unit tests can feed JSON directly without spawning
    ///     the vswhere process.
    /// </remarks>
    internal static IReadOnlyList<VsInstance> ParseInstances(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

        List<VsInstance> result = [];
        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("installationPath", out JsonElement pathProp) ||
                !element.TryGetProperty("installationVersion", out JsonElement versionProp))
                continue;

            string? path = pathProp.GetString();
            string? versionString = versionProp.GetString();
            if (path is null || versionString is null || !Version.TryParse(versionString, out Version? version)) continue;

            string name = element.TryGetProperty("displayName", out JsonElement nameProp) && nameProp.GetString() is { } n
                ? n
                : "Visual Studio";

            result.Add(new VsInstance(path, version, name));
        }

        return result;
    }

    // Synchronous on purpose, and the reason ChildProcess carries a synchronous arm at all. This probe
    // runs inside MsBuildBootstrap, which the CLI and the xUnit adapter both call from a NoInlining JIT
    // quarantine: no MSBuild type may be resolvable before MSBuildLocator has registered, so that chain
    // cannot become asynchronous without either breaking the quarantine or blocking on a Task somewhere
    // in it. ChildProcess.Run blocks on the process handle and never on a Task, so this path stays
    // within the no-blocking-waits rule while keeping the quarantine intact — and it still gets the
    // closed stdin that keeps a child from inheriting (and wedging on) the MCP server's live JSON-RPC
    // pipe, plus a bounded wait and a kill-tree on expiry.
    private static string RunVsWhere()
    {
        ProcessStartInfo psi = new(VsWherePath, "-all -prerelease -format json -products *")
        {
            CreateNoWindow = true
        };

        ChildProcess.ProcessResult result = ChildProcess.Run(psi, QueryTimeout);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"vswhere exited with code {result.ExitCode}: {result.StandardError}");

        return result.StandardOutput;
    }
}
