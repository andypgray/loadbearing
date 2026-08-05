using Xunit.Sdk;
using Xunit.v3;
using Zphil.LoadBearing.Tests.TestSupport;

[assembly: TestPipelineStartup(typeof(FixtureRestoreStartup))]

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Restores the checked-in fixture solutions once, at the start of the discover/run pipeline —
///     after the runner's assembly-info probe, before any test executes.
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
