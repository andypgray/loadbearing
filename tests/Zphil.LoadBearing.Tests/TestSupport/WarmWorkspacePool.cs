using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     A small, process-wide set of warm <see cref="WorkspaceSession" />s, keyed by solution path — the
///     harness's answer to the suite's dominant cost.
/// </summary>
/// <remarks>
///     <para>
///         <b>What it buys.</b> A measured run opened <b>146</b> <c>MSBuildWorkspace</c>es (~2.5 s each
///         for the MyApp fixture, ~17-24 s for this repo's own solution) against a serial path of ~435 s.
///         Nearly all of them re-opened a solution some earlier test in the same class had already loaded.
///     </para>
///     <para>
///         <b>Correct by reconcile, not by luck.</b> Every acquisition goes through
///         <see cref="WorkspaceSession.GetCurrentAsync" />, which reconciles against disk before it answers:
///         a structural delta, an added source file, or a deleted document reloads the workspace wholesale,
///         and an in-place edit is folded into a fresh snapshot. That is the same machinery the warm MCP
///         server runs in production, so a test that edits the tree — including
///         <see cref="TempFixtureWorkspace" />'s reset back to pristine between tests — sees exactly what a
///         freshly opened workspace would.
///     </para>
///     <para>
///         <b>The reset is visible.</b> <see cref="TempFixtureWorkspace" /> restores a mutated file with
///         <c>File.Copy</c>, which carries the pristine source's write time over, so the restored file
///         differs from the recorded fingerprint in both mtime and (usually) length. <c>FileFreshness</c>
///         treats any mtime difference as a change — a backwards timestamp included — so the sweep re-reads
///         and folds. Its one blind spot (equal mtime <em>and</em> equal length <em>and</em> different bytes
///         on an already-promoted file) cannot arise from a restore, which by definition puts the bytes
///         back. <c>WarmWorkspacePoolTests</c> pins this end to end.
///     </para>
///     <para>
///         <b>Not every test may use it.</b> A test that pins the
///         <see cref="WorkspaceLoader.LoadCount" /> delta, that needs a cold run as an oracle against a
///         warm one, or that has workspace loading itself as its subject must keep opening real
///         workspaces; each such test says so at its own call site.
///     </para>
///     <para>
///         <b>Bounded.</b> At most <see cref="Capacity" /> sessions are live, evicted least-recently-used
///         first, because each holds an <c>MSBuildWorkspace</c> and its out-of-process BuildHost. Three
///         covers the observed access pattern — a class's leased fixture copy, the shared output-tree
///         solution, and this repo's own — without a class's alternating paths thrashing.
///     </para>
/// </remarks>
internal static class WarmWorkspacePool
{
    private const int Capacity = 3;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    // Least-recently-used first; the tail is the most recent acquisition.
    private static readonly List<Entry> Live = [];

    /// <summary>
    ///     The pool as an <see cref="ISolutionSource" />, for handing to <see cref="CliEntry" /> or to any
    ///     runner that takes one. The handle it returns owns nothing (the pool outlives the call) and carries
    ///     no warm fragment extractor, so extraction stays the same full walk a cold run performs — only the
    ///     workspace open is shared.
    /// </summary>
    internal static ISolutionSource Source { get; } = new PooledSolutionSource();

    /// <summary>
    ///     The number of acquisitions served from an already-loaded session. With
    ///     <see cref="WorkspaceLoader.LoadCount" /> this is the pool's whole value proposition in two numbers.
    /// </summary>
    internal static long ReuseCount { get; private set; }

    /// <summary>
    ///     The reconciled snapshot for <paramref name="solutionPath" />, loading it if no session holds it.
    /// </summary>
    internal static async Task<WorkspaceSnapshot> GetCurrentAsync(string solutionPath, CancellationToken ct)
    {
        string key = Path.GetFullPath(solutionPath);

        await Gate.WaitAsync(ct);
        try
        {
            WorkspaceSession session = await RentAsync(key);
            return await session.GetCurrentAsync(key, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     Disposes every session whose solution lives under <paramref name="directory" />, releasing the
    ///     workspaces and their BuildHosts.
    /// </summary>
    /// <remarks>
    ///     Call this before rewriting or deleting a tree, so a warm workspace can never be the reason a
    ///     file is locked.
    /// </remarks>
    internal static void DropUnder(string directory)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;

        Gate.Wait();
        try
        {
            var doomed = Live.Where(entry => entry.Key.StartsWith(prefix, PathComparison.Comparison)).ToList();
            foreach (Entry entry in doomed)
            {
                Live.Remove(entry);
                Dispose(entry);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Disposes every live session.</summary>
    internal static void DropAll()
    {
        Gate.Wait();
        try
        {
            foreach (Entry entry in Live) Dispose(entry);
            Live.Clear();
        }
        finally
        {
            Gate.Release();
        }
    }

    // Caller holds the gate. Deliberately uncancellable: an eviction half-done would leave a workspace
    // undisposed and its BuildHost orphaned, and the work is a dispose plus a list edit.
    private static async Task<WorkspaceSession> RentAsync(string key)
    {
        int index = Live.FindIndex(entry => string.Equals(entry.Key, key, PathComparison.Comparison));
        if (index >= 0)
        {
            Entry existing = Live[index];
            Live.RemoveAt(index);
            Live.Add(existing); // most recently used
            ReuseCount++;
            return existing.Session;
        }

        while (Live.Count >= Capacity)
        {
            Entry evicted = Live[0];
            Live.RemoveAt(0);
            await evicted.Session.DisposeAsync();
        }

        var minted = new Entry(key, new WorkspaceSession());
        Live.Add(minted);
        return minted.Session;
    }

    // Blocking dispose: the pool's sync entry points run between tests, never inside a workspace call, and a
    // session's own DisposeAsync is already bounded, so there is nothing here to deadlock against.
    private static void Dispose(Entry entry)
    {
        entry.Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed record Entry(string Key, WorkspaceSession Session);

    /// <summary>
    ///     The pool behind the <see cref="ISolutionSource" /> seam: discover exactly as the cold path does
    ///     (so a discovery failure raises the byte-identical <see cref="UserErrorException" />), then serve
    ///     the pooled session's reconciled snapshot.
    /// </summary>
    private sealed class PooledSolutionSource : ISolutionSource
    {
        public async Task<SolutionHandle> AcquireAsync(string? solution, string workingDirectory, CancellationToken ct)
        {
            string solutionPath = ModelPipeline.DiscoverSolution(solution, workingDirectory);
            WorkspaceSnapshot snapshot = await GetCurrentAsync(solutionPath, ct);
            return new SolutionHandle(snapshot.Solution, solutionPath, snapshot.Diagnostics, null);
        }
    }
}
