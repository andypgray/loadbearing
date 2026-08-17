using Xunit;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The running test's cancellation token, under a name short enough to spell at every call site that
///     takes one. xUnit v3 signals this token when a test overruns its timeout or the run is cut short, so
///     threading it through is what turns a hung await into one failed test rather than a stalled run.
/// </summary>
/// <remarks>
///     Imported project-wide by <c>GlobalUsings.cs</c>, so <c>Ct</c> resolves bare in every test file and
///     this type's name is spelled exactly once. It holds one member for that reason: a
///     <c>using static</c> imports everything a type exposes, so anything added beside <c>Ct</c>
///     would arrive unannounced in all of them.
/// </remarks>
internal static class TestCancellation
{
    /// <summary>The token of the currently-executing test.</summary>
    public static CancellationToken Ct => TestContext.Current.CancellationToken;
}
