using System.CommandLine;
using System.CommandLine.Parsing;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The invocation core, factored out of <c>Program</c> so the in-process e2e tests exercise the
///     real command tree, parse-error remap, and action dispatch through a redirected
///     <see cref="InvocationConfiguration" />. Building and parsing touch no Roslyn type.
/// </summary>
internal static class CliEntry
{
    public static async Task<int> InvokeAsync(string[] args, InvocationConfiguration configuration)
    {
        ParseResult parseResult = CommandFactory.BuildRootCommand().Parse(args);

        // Remap System.CommandLine's default parse-error exit code (1) to 2; 1 means "violations found".
        if (parseResult.Errors.Count > 0)
        {
            foreach (ParseError error in parseResult.Errors) configuration.Error.WriteLine(error.Message);
            return 2;
        }

        return await parseResult.InvokeAsync(configuration);
    }
}