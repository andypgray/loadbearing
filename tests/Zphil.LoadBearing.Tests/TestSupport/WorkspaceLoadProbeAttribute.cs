using System.Collections.Concurrent;
using System.Reflection;
using Xunit.v3;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

[assembly: WorkspaceLoadProbe]

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Attributes every <c>MSBuildWorkspace</c> load to the test that caused it, so the suite's dominant
///     cost can be measured rather than estimated. Assembly-scoped, and inert unless
///     <see cref="ProbeFileVariable" /> names an output file — an off run pays one environment read per
///     test and nothing else.
/// </summary>
/// <remarks>
///     <para>
///         Set <c>LOADBEARING_LOAD_PROBE</c> to a path and run the suite; the file is written at pipeline
///         shutdown as TSV (<c>class</c>, <c>method</c>, <c>loads</c>, <c>ms</c>), one row per test that
///         opened at least one workspace, plus a trailing total. Loads only ever happen inside the
///         <see cref="SerialCollection">"Serial"</see> collection, so attribution is exact there; a
///         parallel test can only ever record zero.
///     </para>
/// </remarks>
internal sealed class WorkspaceLoadProbeAttribute : BeforeAfterTestAttribute
{
    /// <summary>Names the TSV the probe writes at shutdown. Unset (the default) disables the probe.</summary>
    internal const string ProbeFileVariable = "LOADBEARING_LOAD_PROBE";

    private static readonly ConcurrentQueue<string> Rows = new();

    private static readonly ConcurrentDictionary<IXunitTest, Reading> Started = new();

    private static long _totalLoads;

    private static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ProbeFileVariable));

    /// <inheritdoc />
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!Enabled) return;

        Started[test] = new Reading(WorkspaceLoader.LoadCount, Environment.TickCount64);
    }

    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!Started.TryRemove(test, out Reading before)) return;

        long loads = WorkspaceLoader.LoadCount - before.Loads;
        if (loads == 0) return;

        Interlocked.Add(ref _totalLoads, loads);
        long elapsed = Environment.TickCount64 - before.Ticks;
        string testClass = methodUnderTest.DeclaringType?.Name ?? "(unknown)";
        Rows.Enqueue($"{testClass}\t{methodUnderTest.Name}\t{loads}\t{elapsed}");
    }

    /// <summary>Writes the collected rows, newest last, with a total line.</summary>
    /// <remarks>A write failure is swallowed, since a diagnostic must never fail a run.</remarks>
    internal static void Flush()
    {
        string? path = Environment.GetEnvironmentVariable(ProbeFileVariable);
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var lines = new List<string> { "class\tmethod\tloads\tms" };
            lines.AddRange(Rows.OrderBy(r => r, StringComparer.Ordinal));
            lines.Add($"TOTAL\t\t{Interlocked.Read(ref _totalLoads)}\t");
            // The other half of the story: how many acquisitions a warm pooled session answered instead.
            lines.Add($"POOL-REUSED\t\t{WarmWorkspacePool.ReuseCount}\t");
            File.WriteAllLines(path, lines);
        }
        catch
        {
            // best-effort: the probe is a diagnostic, never a reason to fail a run.
        }
    }

    private readonly record struct Reading(long Loads, long Ticks);
}
