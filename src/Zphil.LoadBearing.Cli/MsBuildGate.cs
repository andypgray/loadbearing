using System.Runtime.CompilerServices;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Replay;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;
using Zphil.LoadBearing.Roslyn.Replay;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The MSBuildLocator quarantine. Neither <c>Program</c> nor the command wiring references a
///     Roslyn/MSBuild type; the command actions call in here. <see cref="MethodImplOptions.NoInlining" />
///     keeps the JIT from resolving the runners (and through them <c>MSBuildWorkspace</c>) until after
///     registration and the source-selection decision have run. In tests MSBuild is already registered by a
///     <c>[ModuleInitializer]</c>, so <see cref="MsBuildBootstrap.EnsureInitialized" /> is a no-op.
/// </summary>
/// <remarks>
///     <para>
///         <b>The replay branch.</b> <c>check</c>/<c>status</c>/<c>graph</c> route through
///         <see cref="SelectSourceAndRunAsync" />, which registers MSBuildLocator once up front and then
///         decides the source over only <c>Replay</c>-namespace + BCL types (capture validation, option
///         inspection), never resolving an <c>MSBuildWorkspace</c> in the decision itself. Every branch needs
///         the registration — the explicit <c>--binlog</c> replay and the structurally-valid capture replay
///         included — because the binlog parser (MSBuild.StructuredLogger, under
///         <c>Basic.CompilerLog.Util</c>) resolves <c>Microsoft.Build.Framework</c> through the locator. What
///         the replay branches never do is open an <c>MSBuildWorkspace</c> or run a design-time build: the
///         bypass is of the <em>build</em>, not of the registration. The quarantine's point is JIT ordering —
///         every runner invocation still crosses a NoInlining stepping stone, so the runner (and thus the
///         workspace types it can reach) resolves only after registration and the decision.
///     </para>
/// </remarks>
internal static class MsBuildGate
{
    /// <summary>
    ///     Which source-selection branch the last <c>check</c>/<c>status</c>/<c>graph</c> run took. Internal
    ///     test observable — output is byte-identical whichever branch runs, so this only tells a test which
    ///     path was chosen; never printed.
    /// </summary>
    internal static GateAcquisition? LastAcquisition { get; private set; }

    // ── check / status / graph: replay-aware source selection ────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunCheckAsync(CheckRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        return SelectSourceAndRunAsync(
            request.Solution, request.WorkingDirectory, request.Binlog, request.NoCache, error,
            source => InvokeCheckAsync(request, output, error, source, ct), ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunStatusAsync(StatusRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        return SelectSourceAndRunAsync(
            request.Solution, request.WorkingDirectory, request.Binlog, request.NoCache, error,
            source => InvokeStatusAsync(request, output, error, source, ct), ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<int> RunGraphAsync(GraphRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        return SelectSourceAndRunAsync(
            request.Solution, request.WorkingDirectory, request.Binlog, request.NoCache, error,
            source => InvokeGraphAsync(request, output, error, source, ct), ct);
    }

    // ── explain / render / baseline: the plain cold path (no --binlog) ───────────────────────────────────

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int> RunExplainAsync(ExplainRequest request, TextWriter output, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return await InvokeExplainAsync(request, output, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int> RunRenderAsync(RenderRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return await InvokeRenderAsync(request, output, error, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int> RunBaselineAsync(BaselineRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        EnsureMsBuildRegistered();
        return await InvokeBaselineAsync(request, output, error, ct);
    }

    // ── the source-selection gate ────────────────────────────────────────────────────────────────────────

    // The pre-decision: an explicit --binlog wins (replay-first, eagerly); otherwise, unless --no-cache, a
    // structurally-valid capture replays lazily, a stale/unreadable one prints its notice and falls back to a
    // design-time build, and an absent one runs the plain cold path. Registration runs once up front: the
    // binlog parser binds Microsoft.Build.Framework through MSBuildLocator, so every branch — replay included
    // — needs it. The decision itself touches only Replay-namespace + BCL types (no MSBuildWorkspace resolves
    // here), and the runner types stay quarantined behind the NoInlining stepping stones until after it.
    private static async Task<int> SelectSourceAndRunAsync(
        string? solutionArgument, string workingDirectory, string? binlog, bool noCache,
        TextWriter error, Func<ISolutionSource, Task<int>> invokeRunner, CancellationToken ct)
    {
        EnsureMsBuildRegistered();

        string? cacheRoot = CacheRootOverride();

        if (!string.IsNullOrWhiteSpace(binlog))
            return await RunExplicitBinlogAsync(
                solutionArgument, workingDirectory, binlog, noCache, cacheRoot, invokeRunner, ct);

        if (!noCache)
        {
            string solutionPath = ModelPipeline.DiscoverSolution(solutionArgument, workingDirectory);
            CaptureValidation capture = ValidateCapture(solutionPath, cacheRoot, ct);
            if (capture.State == CaptureState.Usable)
                return await RunCaptureReplayAsync(capture.BinlogCopyPath!, error, invokeRunner);
            if (capture.State == CaptureState.Invalid)
                return await RunNoticeColdAsync(capture.Notice!, error, invokeRunner);
        }

        return await RunColdAsync(invokeRunner);
    }

    // Explicit --binlog: replay the user's binlog (wrapping every failure loudly), then — unless --no-cache —
    // ingest it as a capture (refusals propagate as exit-2 UserErrorExceptions), then run over the replayed
    // solution. Eager, not lazy-in-source, so refusals and persistence fire deterministically even when the
    // fragment cache would hit and never acquire. The gate owns the replayed solution for the run's duration.
    private static async Task<int> RunExplicitBinlogAsync(
        string? solutionArgument, string workingDirectory, string binlog, bool noCache, string? cacheRoot,
        Func<ISolutionSource, Task<int>> invokeRunner, CancellationToken ct)
    {
        LastAcquisition = GateAcquisition.ExplicitReplay;

        string binlogFullPath = Path.GetFullPath(binlog);
        if (!File.Exists(binlogFullPath))
            throw new UserErrorException(BinlogReplayMessages.MissingFileMessage(binlog));

        string solutionPath = ModelPipeline.DiscoverSolution(solutionArgument, workingDirectory);

        var diagnostics = new List<string>();
        ReplayedSolution replayed = ReplayOrThrow(binlogFullPath, binlog, diagnostics, ct);
        try
        {
            if (!noCache)
                new BinlogCaptureStore(solutionPath, cacheRoot).Ingest(replayed.Solution, binlogFullPath, binlog, ct);

            return await invokeRunner(new ReplayedSolutionSource(replayed.Solution, solutionPath, diagnostics));
        }
        finally
        {
            replayed.Dispose();
        }
    }

    // A structurally-valid capture: replay it lazily, so a fragment-cache hit stays replay-free and
    // byte-identical to a plain cached run. A runtime replay failure of the validated copy is recoverable —
    // acquisition precedes all rendering, so we print the notice and re-run cold (MSBuildLocator is already
    // registered from the up-front call); the two runner invocations build independent runners and the failed
    // one wrote nothing, so the retry cannot double-render.
    private static async Task<int> RunCaptureReplayAsync(
        string binlogCopyPath, TextWriter error, Func<ISolutionSource, Task<int>> invokeRunner)
    {
        LastAcquisition = GateAcquisition.CaptureReplay;

        var source = new LazyCaptureReplaySource(binlogCopyPath);
        try
        {
            return await invokeRunner(source);
        }
        catch (CaptureReplayFailedException ex)
        {
            error.WriteLine($"warning: {ex.Message}");
            LastAcquisition = GateAcquisition.CaptureReplayFellBackToCold;
            return await invokeRunner(new ColdSolutionSource());
        }
        finally
        {
            source.Replayed?.Dispose();
        }
    }

    // A stale/unreadable capture: run cold, printing the capture's notice at workspace-acquisition time
    // (never at startup) so a fragment-cache hit acquires nothing and prints nothing.
    private static async Task<int> RunNoticeColdAsync(
        string notice, TextWriter error, Func<ISolutionSource, Task<int>> invokeRunner)
    {
        LastAcquisition = GateAcquisition.NoticeCold;
        return await invokeRunner(new NoticingSolutionSource(notice, error, new ColdSolutionSource()));
    }

    // No capture and no explicit binlog (or --no-cache): today's plain cold path, silent and byte-identical.
    private static async Task<int> RunColdAsync(Func<ISolutionSource, Task<int>> invokeRunner)
    {
        LastAcquisition = GateAcquisition.Cold;
        return await invokeRunner(new ColdSolutionSource());
    }

    private static ReplayedSolution ReplayOrThrow(
        string binlogFullPath, string binlogArgument, List<string> diagnostics, CancellationToken ct)
    {
        try
        {
            return BinlogReplayer.Replay(binlogFullPath, diagnostics.Add, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UserErrorException(BinlogReplayMessages.ReplayFailedMessage(binlogArgument, ex.Message), ex);
        }
    }

    private static CaptureValidation ValidateCapture(string solutionPath, string? cacheRoot, CancellationToken ct)
    {
        try
        {
            return new BinlogCaptureStore(solutionPath, cacheRoot).Validate(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // No resolvable cache location (or an unexpected store-construction failure): the silent cold path.
            return CaptureValidation.Absent();
        }
    }

    private static string? CacheRootOverride()
    {
        IEnvironment environment = new SystemEnvironment();
        string? cacheRoot = environment.GetVariable(CodebaseSource.CacheDirectoryVariable);
        return string.IsNullOrWhiteSpace(cacheRoot) ? null : cacheRoot;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureMsBuildRegistered()
    {
        MsBuildBootstrap.EnsureInitialized();
    }

    // ── runner stepping stones (NoInlining: the runner and its workspace types resolve here, post-decision) ──

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeCheckAsync(
        CheckRequest request, TextWriter output, TextWriter error, ISolutionSource source, CancellationToken ct)
    {
        return new CheckRunner(output, error, source).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeStatusAsync(
        StatusRequest request, TextWriter output, TextWriter error, ISolutionSource source, CancellationToken ct)
    {
        return new StatusRunner(output, error, source).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeGraphAsync(
        GraphRequest request, TextWriter output, TextWriter error, ISolutionSource source, CancellationToken ct)
    {
        return new GraphRunner(output, error, source).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeExplainAsync(ExplainRequest request, TextWriter output, CancellationToken ct)
    {
        return new ExplainRunner(output).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeRenderAsync(RenderRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        return new RenderRunner(output, error).RunAsync(request, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> InvokeBaselineAsync(BaselineRequest request, TextWriter output, TextWriter error, CancellationToken ct)
    {
        return new BaselineRunner(output, error).RunAsync(request, ct);
    }
}