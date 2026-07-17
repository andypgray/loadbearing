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

    public static string FrozenSpecDll => Metadata("FrozenSpecPath");

    public static string DerivedSpecDll => Metadata("DerivedSpecPath");

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