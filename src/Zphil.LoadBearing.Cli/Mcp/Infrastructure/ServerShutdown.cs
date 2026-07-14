using Serilog;

namespace Zphil.LoadBearing.Cli.Mcp.Infrastructure;

/// <summary>
///     Shared exit path for both watchdogs. Waits for an in-flight tool call to finish, runs any
///     registered async disposers (with a per-disposer timeout), and flushes Serilog before terminating
///     the process.
/// </summary>
/// <remarks>
///     <para>
///         Both <see cref="IdleTimeoutWatchdog" /> and <see cref="ParentProcessWatcher" /> route through
///         <see cref="ExitWith(string)" /> instead of calling <c>Environment.Exit</c> directly, so a
///         mid-flight tool call drains and the logger flushes first. LoadBearing loads a fresh workspace
///         per tool call and caches nothing, so no disposer is registered in v1 — but the seam is kept so
///         a future long-lived resource (a workspace cache, Phase 10) can register its dispose here.
///     </para>
///     <para>
///         A per-disposer bound keeps shutdown deterministic even if a disposer hangs.
///     </para>
/// </remarks>
internal static class ServerShutdown
{
    // 12s bound per disposer: sized to a long-lived MSBuild server's measured worst case, where a
    // workspace dispose (drain pending updates + acquire the mutation gate, then dispose the
    // MSBuildWorkspace + BuildHost child) takes ~10s. LoadBearing registers no disposer today, but the
    // headroom is kept for a future workspace-cache disposer so a 5s bound could not abandon a dispose
    // partway.
    private static readonly TimeSpan DefaultDisposalTimeout = TimeSpan.FromSeconds(12);

    // How long ExitWith waits for in-flight tool calls to finish before running disposers. Bounds the
    // window that lets a mid-flight call commit; 5s keeps shutdown from being held hostage by a stuck call.
    private static readonly TimeSpan DefaultInFlightDrainTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InFlightPollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly Lock DisposersLock = new();
    private static readonly List<Func<ValueTask>> Disposers = [];
    private static int s_hasExited;

    /// <summary>
    ///     True once <see cref="ExitWith(string)" /> has been accepted by the gate. Subsequent
    ///     callers return silently — disposal runs at most once, no matter how many watchdogs
    ///     race to exit.
    /// </summary>
    internal static bool HasExited => Volatile.Read(ref s_hasExited) != 0;

    /// <summary>
    ///     Adds <paramref name="dispose" /> to the list of disposers run by
    ///     <see cref="ExitWith(string)" />. Multiple registrations run in registration order.
    /// </summary>
    /// <param name="dispose">
    ///     Asynchronous callback invoked during shutdown to release a resource. Faults and
    ///     timeouts are logged at Warning but never block exit.
    /// </param>
    public static void RegisterDisposer(Func<ValueTask> dispose)
    {
        ArgumentNullException.ThrowIfNull(dispose);
        lock (DisposersLock)
        {
            Disposers.Add(dispose);
        }
    }

    /// <summary>
    ///     Production entry: drains in-flight calls, runs registered disposers with a bound each, flushes
    ///     Serilog, then calls <see cref="Environment.Exit" /> with code 0.
    /// </summary>
    /// <param name="reason">
    ///     Short label describing why the server is shutting down (e.g. "idle timeout").
    ///     Logged at Warning so post-mortems can identify the trigger.
    /// </param>
    public static void ExitWith(string reason)
    {
        ExitWith(reason, static () => Environment.Exit(0), DefaultDisposalTimeout, DefaultInFlightDrainTimeout);
    }

    /// <summary>
    ///     Test seam: lets tests substitute the terminal action and shrink the disposal / in-flight-drain
    ///     timeouts so they can verify the gate, in-flight drain, disposal-then-exit order, and timeout
    ///     fallback without killing the test host. <paramref name="inFlightDrainTimeout" /> defaults to
    ///     <see cref="DefaultInFlightDrainTimeout" /> when null.
    /// </summary>
    internal static void ExitWith(string reason, Action exit, TimeSpan disposalTimeout, TimeSpan? inFlightDrainTimeout = null)
    {
        if (Interlocked.CompareExchange(ref s_hasExited, 1, 0) != 0) return;

        Log.Warning("Shutting down: {Reason}", reason);

        // Let an in-flight tool call finish before tearing the process down. Bounded so a stuck call
        // can't hold shutdown hostage.
        WaitForInFlightCalls(inFlightDrainTimeout ?? DefaultInFlightDrainTimeout);

        List<Func<ValueTask>> snapshot;
        lock (DisposersLock)
        {
            snapshot = [.. Disposers];
        }

        foreach (var disposer in snapshot) RunDisposerBounded(disposer, disposalTimeout);

        Log.CloseAndFlush();
        exit();
    }

    /// <summary>
    ///     Spins (50 ms poll) until no tool call is in flight or <paramref name="timeout" /> elapses.
    ///     Synchronous by design — this is the terminal shutdown path, so blocking the caller is fine.
    /// </summary>
    private static void WaitForInFlightCalls(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return;

        long deadlineTicks = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (IdleTimeoutWatchdog.InFlightCount > 0 && Environment.TickCount64 < deadlineTicks) Thread.Sleep(InFlightPollInterval);
    }

    private static void RunDisposerBounded(Func<ValueTask> disposer, TimeSpan timeout)
    {
        try
        {
            Task disposeTask = disposer().AsTask();
            if (!disposeTask.Wait(timeout)) Log.Warning("Disposer timed out after {Timeout}; continuing exit", timeout);
        }
        catch (AggregateException ae)
        {
            // Task.Wait wraps disposer faults; unwrap so the log shows the real exception.
            Log.Warning(ae.GetBaseException(), "Disposer faulted; continuing exit");
        }
        catch (Exception ex)
        {
            // Synchronous throw from the disposer itself (before it returned a Task).
            Log.Warning(ex, "Disposer threw synchronously; continuing exit");
        }
    }

    internal static void ResetForTests()
    {
        lock (DisposersLock)
        {
            Disposers.Clear();
        }

        Interlocked.Exchange(ref s_hasExited, 0);
    }
}