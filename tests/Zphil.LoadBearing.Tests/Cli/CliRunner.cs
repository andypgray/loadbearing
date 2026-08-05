using System.CommandLine;
using System.Reflection;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>The result of an in-process CLI invocation.</summary>
internal sealed record CliResult(int Exit, string Out, string Err);

/// <summary>
///     Drives the CLI in-process through the real <see cref="CliEntry" /> and command tree, capturing
///     stdout/stderr via a redirected <see cref="InvocationConfiguration" /> (no child process). Also
///     surfaces the MyApp solution path and the fixture spec DLL paths the build bakes into this
///     assembly's metadata.
/// </summary>
/// <remarks>
///     <b>Warm by default.</b> <see cref="InvokeAsync" /> hands the CLI the shared
///     <see cref="WarmWorkspacePool" /> as its host source, so a class's many invocations reuse one loaded
///     workspace (reconciled against disk on every call) instead of opening one each. Nothing else about
///     the run changes: the same command tree, the same runners, the same full extraction, the same
///     stdout/stderr. A test whose subject <em>is</em> the loading — a load-count pin, a
///     workspace-acquisition count — calls <see cref="InvokeColdAsync" /> instead and gets today's fresh
///     one-shot workspace per invocation.
/// </remarks>
internal static class CliRunner
{
    public static string MyAppSolution =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MyApp", "MyApp.sln");

    public static string ViolatedSpecDll => Metadata("ViolatedSpecPath");

    public static string CleanSpecDll => Metadata("CleanSpecPath");

    public static string RenderSpecDll => Metadata("RenderSpecPath");

    public static string LayerSpecDll => Metadata("LayerSpecPath");

    public static string QuarantinedSpecDll => Metadata("QuarantinedSpecPath");

    public static string DerivedSpecDll => Metadata("DerivedSpecPath");

    /// <summary>The net48 product assembly the legacy spec <c>typeof()</c>s.</summary>
    public static string LegacyProductDll => Metadata("LegacyProductPath");

    /// <summary>The net48 spec DLL, built at the C# 7.3 default.</summary>
    public static string LegacySpecDll => Metadata("LegacySpecPath");

    /// <summary>The net48 spec DLL whose <c>Define()</c> anchors a type that cannot load on .NET.</summary>
    public static string LegacyBrokenSpecDll => Metadata("LegacyBrokenSpecPath");

    /// <summary>The net48 pattern-only spec that drives <c>check</c> against the ClassicApp solution.</summary>
    public static string ClassicAppSpecDll => Metadata("ClassicAppSpecPath");

    /// <summary>The MyApp domain assembly — an ordinary product DLL, carrying no spec at all.</summary>
    public static string MyAppDomainDll => Metadata("MyAppDomainPath");

    /// <summary>The non-SDK-style .NET Framework fixture solution, copied to the test output as content.</summary>
    public static string ClassicAppSolution =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacySolutions", "ClassicApp", "ClassicApp.sln");

    /// <summary>Runs the CLI over the shared warm workspace pool — the default; see the type's remarks.</summary>
    public static Task<CliResult> InvokeAsync(params string[] args)
    {
        return RunAsync(WarmWorkspacePool.Source, args);
    }

    /// <summary>
    ///     Runs the CLI with no host source, so every workspace command opens (and disposes) its own
    ///     <c>MSBuildWorkspace</c>. For tests that assert on <see cref="Zphil.LoadBearing.Roslyn.WorkspaceLoader.LoadCount" />
    ///     or otherwise measure whether a design-time build ran.
    /// </summary>
    public static Task<CliResult> InvokeColdAsync(params string[] args)
    {
        return RunAsync(null, args);
    }

    private static async Task<CliResult> RunAsync(ISolutionSource? hostSource, string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var configuration = new InvocationConfiguration { Output = output, Error = error };

        int exit = await CliEntry.InvokeAsync(args, configuration, hostSource);
        return new CliResult(exit, output.ToString(), error.ToString());
    }

    private static string Metadata(string key)
    {
        string? value = typeof(CliRunner).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(a => a.Key == key)?.Value;

        if (string.IsNullOrEmpty(value)) throw new InvalidOperationException($"Assembly metadata '{key}' was not baked in by the build.");

        return value;
    }
}
