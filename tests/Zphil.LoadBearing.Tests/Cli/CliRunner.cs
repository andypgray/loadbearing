using System.CommandLine;
using System.Reflection;
using Zphil.LoadBearing.Cli;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>The result of an in-process CLI invocation.</summary>
internal sealed record CliResult(int Exit, string Out, string Err);

/// <summary>
///     Drives the CLI in-process through the real <see cref="CliEntry" /> and command tree, capturing
///     stdout/stderr via a redirected <see cref="InvocationConfiguration" /> (no child process). Also
///     surfaces the MyApp solution path and the fixture spec DLL paths the build bakes into this
///     assembly's metadata.
/// </summary>
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

    /// <summary>The net48 pattern-only spec that drives <c>check</c> against the ClassicApp solution.</summary>
    public static string ClassicAppSpecDll => Metadata("ClassicAppSpecPath");

    /// <summary>The non-SDK-style .NET Framework fixture solution, copied to the test output as content.</summary>
    public static string ClassicAppSolution =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacySolutions", "ClassicApp", "ClassicApp.sln");

    public static async Task<CliResult> InvokeAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var configuration = new InvocationConfiguration { Output = output, Error = error };

        int exit = await CliEntry.InvokeAsync(args, configuration);
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