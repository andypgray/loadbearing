using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Hands out run-scoped temp directories under one <c>loadbearing-tests/</c> parent, and sweeps the
///     roots earlier runs left behind.
/// </summary>
/// <remarks>
///     <para>
///         Every category (fixture copies, the extraction cache) gets
///         <c>%TEMP%/loadbearing-tests/&lt;category&gt;/&lt;run-id&gt;</c>, where the run id is one GUID per
///         test process. Runs stay isolated from each other, which is what the per-run cache directory was
///         always for.
///     </para>
///     <para>
///         <b>Why a sweep rather than only a teardown.</b> A run that is killed — or whose tree a lingering
///         MSBuild handle still holds — cannot delete its own root, so teardown alone leaks a little on every
///         such run and never recovers. Before this existed the two categories had accumulated 160 cache roots
///         (1.3 GB) and 606 stranded fixture copies. The sweep runs in the background at startup so a cold
///         process pays nothing for it, and only touches roots older than <see cref="StaleAfter" /> — never a
///         concurrent run's.
///     </para>
/// </remarks>
internal static class TestTempRoot
{
    private const string ParentFolder = "loadbearing-tests";

    // Comfortably longer than any suite run, so a concurrently-running second test process is never swept.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(4);

    // One id per test process: every category this run asks for lands under the same run directory.
    private static readonly string RunId = Guid.NewGuid().ToString("N");

    // The temp base, resolved through any symlinked ancestor once per run, so every path handed out shares
    // one spelling. See TempFixtureWorkspace.RealTempRoot for why the harness needs this beyond production.
    private static readonly string TempBase =
        PathCanonicalizer.Resolve(Path.TrimEndingDirectorySeparator(Path.GetTempPath()));

    private static readonly Lock SweepGate = new();

    private static bool sweepStarted;

    /// <summary>
    ///     The run-scoped directory for <paramref name="category" />, created if absent. The first call also
    ///     starts the background sweep of stale roots from earlier runs.
    /// </summary>
    internal static string For(string category)
    {
        string categoryRoot = Path.Combine(TempBase, ParentFolder, category);
        string runRoot = Path.Combine(categoryRoot, RunId);
        Directory.CreateDirectory(runRoot);
        StartSweepOnce();
        return runRoot;
    }

    private static void StartSweepOnce()
    {
        lock (SweepGate)
        {
            if (sweepStarted) return;
            sweepStarted = true;
        }

        // Fire-and-forget: a sweep of a large backlog can take seconds, and no test should wait for it.
        _ = Task.Run(SweepStaleRoots);
    }

    private static void SweepStaleRoots()
    {
        try
        {
            string parent = Path.Combine(TempBase, ParentFolder);
            if (!Directory.Exists(parent)) return;

            DateTime cutoff = DateTime.UtcNow - StaleAfter;
            foreach (string categoryRoot in Directory.EnumerateDirectories(parent))
            foreach (string runRoot in Directory.EnumerateDirectories(categoryRoot))
            {
                if (Path.GetFileName(runRoot) == RunId) continue;
                if (Directory.GetLastWriteTimeUtc(runRoot) > cutoff) continue;

                try
                {
                    Directory.Delete(runRoot, true);
                }
                catch
                {
                    // best-effort: a root another process still holds is left for the next run to retry.
                }
            }
        }
        catch
        {
            // best-effort: the sweep is hygiene, never a reason to fail a run.
        }
    }
}
