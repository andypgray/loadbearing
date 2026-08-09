namespace Zphil.LoadBearing.Cli;

/// <summary>
///     What every verb's runner is: a pipeline over one <see cref="ISolutionSource" />. The source is
///     injected so a host that already holds a solution — the MCP server's warm session, the in-process e2e
///     harness's pool — can serve the run, and defaulted so the real CLI needs no wiring at all. Nothing
///     else is shared here: what each verb does with the acquired codebase is the verb.
/// </summary>
/// <param name="source">
///     The source to acquire through, or <c>null</c> for the one-shot <see cref="ColdSolutionSource" />
///     every plain CLI invocation uses.
/// </param>
internal abstract class WorkspaceRunner(ISolutionSource? source)
{
    /// <summary>The seam this run acquires its solution — and its spec model — through.</summary>
    protected ISolutionSource SolutionSource { get; } = source ?? new ColdSolutionSource();
}
