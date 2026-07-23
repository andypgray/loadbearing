using Xunit;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The one non-parallel collection for every test that spawns an OS child process or loads an MSBuild
///     workspace. Membership is <c>[Collection]</c>(<c>"Serial"</c>).
/// </summary>
/// <remarks>
///     <para>
///         <b>Why one collection, not several.</b> <c>xunit.runner.json</c> parallelizes test collections,
///         and a <c>DisableParallelization</c> collection still runs concurrently with <em>other</em>
///         collections — only its own tests serialize. So two heavy collections could still overlap. Every
///         test below therefore shares this single collection, which is the only way to guarantee no two of
///         them run at once.
///     </para>
///     <para>
///         <b>What breaks without it.</b> Tests that spawn a child process with redirected stdout/stderr
///         (<see cref="TempGitRepo" /> → <c>git</c>, <see cref="FixtureRestorer" /> → <c>dotnet restore</c>,
///         <see cref="Mcp.Infrastructure.ParentProcessWatcherTests" /> → <c>cmd</c>) deadlock when a
///         workspace-loading test concurrently spawns a long-lived Roslyn <c>BuildHost</c>: the BuildHost
///         (or a reused MSBuild node) inherits a duplicate of the child's stdout write handle, so the read
///         never sees EOF and the whole run hangs. <see cref="ProcessRunner" /> bounds each read so the hang
///         becomes a fast timeout; serializing here removes the race entirely so the timeout never trips.
///     </para>
///     <para>
///         It also subsumes the former <c>WatchdogStatics</c> group:
///         <see cref="Zphil.LoadBearing.Cli.Mcp.Infrastructure.IdleTimeoutWatchdog" />'s
///         process-global counter is read/reset by the watchdog suites and bumped by the MCP call filter the
///         parity tests drive, so those must not interleave either. They belong to the same serial world.
///     </para>
/// </remarks>
[CollectionDefinition("Serial", DisableParallelization = true)]
public sealed class SerialCollection;