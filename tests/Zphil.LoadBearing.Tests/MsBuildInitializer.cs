using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Xunit;
using Zphil.LoadBearing.Roslyn;
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

        // NB: fixture restore is deliberately NOT done here. A [ModuleInitializer] runs during the
        // runner's assembly-info probe too, whose 60s no-response deadline a cold restore blows past
        // (timing out discovery so zero tests run). It runs from FixtureRestoreStartup, an xUnit v3
        // pipeline-startup hook that fires only in the discover/run pass. Keep this fast.
    }
}