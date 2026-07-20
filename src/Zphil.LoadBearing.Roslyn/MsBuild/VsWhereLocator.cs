using System.Diagnostics;
using System.Text.Json;

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

    private static string RunVsWhere()
    {
        ProcessStartInfo psi = new(VsWherePath, "-all -prerelease -format json -products *")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process proc = Process.Start(psi)
                             ?? throw new InvalidOperationException($"Failed to start {VsWherePath}");

        using var timeout = new CancellationTokenSource(10_000);
        // Drain both streams concurrently AND cancellably. The old code read stdout with a synchronous,
        // unbounded ReadToEnd() *before* the WaitForExit timeout — so a vswhere child whose stdout write
        // handle was inherited by a concurrently-spawned long-lived process never reached EOF and wedged
        // here forever, with the timeout below unreachable. The token now bounds every wait.
        var outputTask = proc.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = proc.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            proc.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            string output = outputTask.GetAwaiter().GetResult();

            if (proc.ExitCode != 0)
            {
                string err = stderrTask.GetAwaiter().GetResult();
                throw new InvalidOperationException($"vswhere exited with code {proc.ExitCode}: {err}");
            }

            return output;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            TryKill(proc);
            throw new TimeoutException("vswhere did not complete within 10 seconds");
        }
    }

    // Best-effort kill of a wedged vswhere process (and any children) before we throw the timeout.
    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch
        {
            // Already exited or inaccessible — nothing to clean up.
        }
    }
}