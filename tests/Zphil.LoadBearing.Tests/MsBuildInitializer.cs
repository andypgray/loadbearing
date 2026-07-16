using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Roslyn.MsBuild;
using Zphil.LoadBearing.Tests.TestSupport;

[assembly: AssemblyFixture(typeof(WorkspaceFixture))]

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     Ensures MSBuild is registered once before any test code runs, routed through
///     <see cref="MsBuildBootstrap" /> so tests exercise the same stable-version selection as
///     production.
/// </summary>
internal static class MsBuildInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!MSBuildLocator.IsRegistered) MsBuildBootstrap.Initialize();

        // Point the persisted extraction cache (Phase 11 D2) at a fresh per-run temp directory so no run
        // reads a cache another run wrote, and so this process never touches a developer's real
        // %LOCALAPPDATA% cache. A side effect this earns for free: every existing golden E2E now runs
        // through the cache path (miss on first touch of a solution, hit on later identical touches), which
        // makes the whole golden suite a continuous cold-vs-hit equality pin — any leak of the cache into
        // observable output shows up as golden churn. Read CLI-side through IEnvironment/SystemEnvironment.
        Environment.SetEnvironmentVariable(
            CodebaseSource.CacheDirectoryVariable,
            Path.Combine(Path.GetTempPath(), "loadbearing-cache-tests", Guid.NewGuid().ToString("N")));

        // NB: fixture restore is deliberately NOT done here. A [ModuleInitializer] runs during the
        // runner's assembly-info probe too, whose 60s no-response deadline a cold restore blows past
        // (timing out discovery so zero tests run). It runs from FixtureRestoreStartup, an xUnit v3
        // pipeline-startup hook that fires only in the discover/run pass. Keep this fast.
    }
}