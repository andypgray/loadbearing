using System.Diagnostics;
using Shouldly;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The Shouldly surface over <see cref="ProcessFileFootprint" />: one assertion, phrased as the property
///     it is checking rather than as a scan.
/// </summary>
internal static class ProcessFileFootprintAssertions
{
    private static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    ///     Asserts that <paramref name="process" /> holds nothing under <paramref name="root" /> — no mapped
    ///     view, no open handle — polling until it is true or the budget expires. Pass
    ///     <paramref name="inheritedHandles" /> (the launcher's own handles, taken before the process
    ///     started) to exclude what it was handed rather than acquired.
    /// </summary>
    /// <remarks>
    ///     Polling rather than a single shot because the subject is a live server: a tool call that has just
    ///     answered may still be closing a file its last statement opened, and a transient handle is not a
    ///     retained one. A clean first scan passes immediately, so the budget costs nothing in the green
    ///     case; only a genuine leak pays it. The failure names every offender with both spellings, because
    ///     when this reds the useful question is <em>which</em> build output is pinned.
    /// </remarks>
    internal static void ShouldEventuallyHoldNoPathsUnder(
        this Process process,
        string root,
        TimeSpan? budget = null,
        IReadOnlyCollection<RetainedPath>? inheritedHandles = null)
    {
        TimeSpan ceiling = budget ?? DefaultBudget;
        long start = Stopwatch.GetTimestamp();

        while (true)
        {
            var retained = ProcessFileFootprint.PathsUnder(process, root);
            if (inheritedHandles is not null)
                retained = ProcessFileFootprint.ExceptInherited(retained, inheritedHandles);

            if (retained.Count == 0) return;

            if (Stopwatch.GetElapsedTime(start) >= ceiling)
                throw new ShouldAssertException(Describe(process, root, retained, ceiling));

            Thread.Sleep(PollInterval);
        }
    }

    private static string Describe(
        Process process, string root, IReadOnlyList<RetainedPath> retained, TimeSpan ceiling)
    {
        var lines = retained
            .OrderBy(path => path.Scan)
            .ThenBy(path => path.ResolvedPath, StringComparer.OrdinalIgnoreCase)
            .Select(path => $"    [{path.Scan}] {path.ResolvedPath}{Environment.NewLine}        raw: {path.RawPath}");

        return $"PID {process.Id} still held {retained.Count} path(s) under '{root}' after "
               + $"{ceiling.TotalSeconds:0.#}s:{Environment.NewLine}"
               + string.Join(Environment.NewLine, lines);
    }
}