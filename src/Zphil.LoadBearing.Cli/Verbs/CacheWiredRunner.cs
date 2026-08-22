using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Pipeline;

namespace Zphil.LoadBearing.Cli.Verbs;

/// <summary>
///     The verbs that front the persisted extraction cache — <c>check</c>, <c>status</c>, <c>graph</c>,
///     <c>render</c>, <c>explain</c> and <c>baseline --add</c> — and everything that follows from it: the
///     <see cref="IEnvironment" /> seam the cache root is read through, and the two observables a test reads
///     to learn which path a run actually took.
/// </summary>
/// <remarks>
///     The observables are deliberately kept symmetric across all of them rather than declared only where a
///     test happens to read them today: they say the same thing about the same seam, and a runner that
///     reported one but not the other would make "which path did this verb take" a question with several
///     different answers. They are never printed — stdout and stderr are byte-identical whichever path a
///     run takes, which is the whole claim the cache has to earn. What the base class does <em>not</em>
///     decide is the policy: each runner spells its own as the <c>noCache</c> argument it passes
///     <see cref="CodebaseSource.CreateWithSpecAsync" />, which is why <c>baseline</c> can front the cache
///     in one mode and refuse to in two.
/// </remarks>
/// <param name="source">The solution source to acquire through, or <c>null</c> for the one-shot cold source.</param>
/// <param name="environment">
///     The environment seam supplying the cache-root override, or <c>null</c> for real process state.
/// </param>
/// <param name="fitter">
///     The response fitter a document-shaped verb offers its coarsening ladder to, or <c>null</c> for the
///     terminal's own — the rung the caller asked for. A verb that writes no document leaves it out.
/// </param>
internal abstract class CacheWiredRunner(
    ISolutionSource? source,
    IEnvironment? environment,
    IResponseFitter? fitter = null)
    : WorkspaceRunner(source)
{
    /// <summary>The environment seam the cache root is resolved through.</summary>
    protected IEnvironment Environment { get; } = environment ?? new SystemEnvironment();

    /// <summary>The fitter deciding which rung of a verb's grain ladder this run's caller actually gets.</summary>
    protected IResponseFitter Fitter { get; } = fitter ?? ResponseFitter.FirstRung;

    /// <summary>The cache path the last run took. Internal test observable; never printed.</summary>
    internal CodebaseSourceOutcome? LastOutcome { get; private set; }

    /// <summary>The projects the last run re-extracted from a workspace. Internal test observable; never printed.</summary>
    internal IReadOnlySet<string> LastReExtractedProjects { get; private set; } = new HashSet<string>();

    /// <summary>
    ///     How many times the last run walked the workspace for fragments — at most one, however many models
    ///     the verb asked its source for. Internal test observable; never printed.
    /// </summary>
    internal int LastExtractionCount { get; private set; }

    /// <summary>
    ///     Publishes the cache observables off a completed run's source. Called after the pipeline, because
    ///     the re-extraction set and the walk count are only final once every
    ///     <see cref="CodebaseSource.ExtractAsync" /> the run makes has run.
    /// </summary>
    /// <param name="source">The source the run acquired its codebase through.</param>
    protected void RecordCacheOutcome(CodebaseSource source)
    {
        LastOutcome = source.Outcome;
        LastReExtractedProjects = source.ReExtractedProjects;
        LastExtractionCount = source.ExtractionCount;
    }
}
