using Zphil.LoadBearing.Cli.Mcp.Infrastructure;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The three verbs that front the persisted extraction cache — <c>check</c>, <c>status</c> and
///     <c>graph</c> — and everything that follows from it: the <see cref="IEnvironment" /> seam the cache
///     root is read through, and the two observables a test reads to learn which path a run actually took.
/// </summary>
/// <remarks>
///     The observables are deliberately kept symmetric across all three rather than declared only where a
///     test happens to read them today: they say the same thing about the same seam, and a runner that
///     reported one but not the other would make "which path did this verb take" a question with three
///     different answers. They are never printed — stdout and stderr are byte-identical whichever path a
///     run takes, which is the whole claim the cache has to earn.
/// </remarks>
/// <param name="source">The solution source to acquire through, or <c>null</c> for the one-shot cold source.</param>
/// <param name="environment">
///     The environment seam supplying the cache-root override, or <c>null</c> for real process state.
/// </param>
internal abstract class CacheWiredRunner(ISolutionSource? source, IEnvironment? environment)
    : WorkspaceRunner(source)
{
    /// <summary>The environment seam the cache root is resolved through.</summary>
    protected IEnvironment Environment { get; } = environment ?? new SystemEnvironment();

    /// <summary>The cache path the last run took. Internal test observable; never printed.</summary>
    internal CodebaseSourceOutcome? LastOutcome { get; private set; }

    /// <summary>The projects the last run re-extracted from a workspace. Internal test observable; never printed.</summary>
    internal IReadOnlySet<string> LastReExtractedProjects { get; private set; } = new HashSet<string>();

    /// <summary>
    ///     Publishes the cache observables off a completed run's source. Called after the pipeline, because
    ///     the re-extraction set is only final once <see cref="CodebaseSource.ExtractAsync" /> has run.
    /// </summary>
    /// <param name="source">The source the run acquired its codebase through.</param>
    protected void RecordCacheOutcome(CodebaseSource source)
    {
        LastOutcome = source.Outcome;
        LastReExtractedProjects = source.ReExtractedProjects;
    }
}
