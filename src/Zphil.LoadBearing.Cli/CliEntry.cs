using System.CommandLine;
using System.CommandLine.Parsing;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;

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
    /// <param name="environment">
    ///     The environment seam the cache-fronted verbs read their cache-root override through, or
    ///     <c>null</c> — what <c>Program</c> passes — for real process state. The e2e harness supplies a fake
    ///     one so a test can isolate its caches without mutating the process environment its neighbours share.
    /// </param>
    public static async Task<int> InvokeAsync(
        string[] args,
        InvocationConfiguration configuration,
        ISolutionSource? hostSource = null,
        IEnvironment? environment = null)
    {
        ParseResult parseResult = CommandFactory.BuildRootCommand(hostSource, environment).Parse(args);

        // Remap System.CommandLine's default parse-error exit code (1) to 2; 1 means "violations found".
        if (parseResult.Errors.Count > 0)
        {
            foreach (ParseError error in parseResult.Errors) await configuration.Error.WriteLineAsync(error.Message);
            return 2;
        }

        return await parseResult.InvokeAsync(configuration);
    }
}
