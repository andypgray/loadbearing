using Xunit.Sdk;
using Xunit.v3;
using Zphil.LoadBearing.Tests.DocHygiene;
using Zphil.LoadBearing.Tests.TestSupport;

[assembly: TestPipelineStartup(typeof(FixtureRestoreStartup))]

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The assembly's one-time setup at the start of the discover/run pipeline — after the runner's
///     assembly-info probe, before any test executes: restores the checked-in fixture solutions and
///     forces the tracked-file inventory's <c>git ls-files</c> spawn while no test can race it.
/// </summary>
/// <remarks>
///     A pipeline-startup hook rather than a <c>[ModuleInitializer]</c>: module initializers also run
///     during the runner's assembly-info probe, whose 60-second no-response deadline a cold fixture
///     restore (minutes) blows past, timing out discovery so zero tests run. Pipeline startup runs only
///     in the discover/run pass, which has no such deadline.
/// </remarks>
internal sealed class FixtureRestoreStartup : ITestPipelineStartup
{
    public ValueTask StartAsync(IMessageSink diagnosticMessageSink)
    {
        FixtureRestorer.EnsureRestored();
        TrackedFiles.EnsureEnumerated();
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync()
    {
        // Release the pooled warm workspaces (and their BuildHost child processes) before the run ends,
        // then publish what the probe collected.
        WarmWorkspacePool.DropAll();
        WorkspaceLoadProbeAttribute.Flush();
        return ValueTask.CompletedTask;
    }
}
