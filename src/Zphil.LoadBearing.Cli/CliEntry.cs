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
    /// <param name="args">The command line, exactly as the shell handed it over.</param>
    /// <param name="configuration">Where the run's stdout/stderr go.</param>
    /// <param name="hostSource">
    ///     The solution source for any run that would otherwise open a fresh one-shot workspace, or
    ///     <c>null</c> — what <c>Program</c> passes — for <see cref="ColdSolutionSource" />. The e2e harness
    ///     supplies a warm one so a test class's many invocations share one loaded workspace.
    /// </param>
    public static async Task<int> InvokeAsync(
        string[] args, InvocationConfiguration configuration, ISolutionSource? hostSource = null)
    {
        ParseResult parseResult = CommandFactory.BuildRootCommand(hostSource).Parse(args);

        // Remap System.CommandLine's default parse-error exit code (1) to 2; 1 means "violations found".
        if (parseResult.Errors.Count > 0)
        {
            foreach (ParseError error in parseResult.Errors) configuration.Error.WriteLine(error.Message);
            return 2;
        }

        return await parseResult.InvokeAsync(configuration);
    }
}
