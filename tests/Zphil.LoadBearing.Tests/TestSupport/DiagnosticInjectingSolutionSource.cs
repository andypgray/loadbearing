using Zphil.LoadBearing.Cli;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     An <see cref="ISolutionSource" /> that wraps a real load, then re-wraps the handle with synthetic
///     workspace-load diagnostics — the one way to drive the incomplete-model gate over a fixture that
///     loads perfectly well. Shared by the gate tests of every verb that has one, so <c>check</c>,
///     <c>baseline</c>, <c>status</c> and <c>graph</c> are exercised against the same injected input rather
///     than each inventing its own.
/// </summary>
/// <remarks>
///     The inner source is the shared warm pool: nothing here asserts on whether a workspace was opened,
///     only on what the gate does with the diagnostics riding the handle. The real handle rides as the
///     owned disposable so the ownership chain still holds — it is the pool's, so disposing it is a no-op.
///     Injected diagnostics never reach a real workspace, so the pooled session stays clean for the next
///     test; a genuinely partial load belongs in
///     <see cref="Zphil.LoadBearing.Tests.Cli.PartialLoadWorkspaceE2ETests" />, which drives a cold load
///     over a fixture the loader really does fail on.
/// </remarks>
internal sealed class DiagnosticInjectingSolutionSource(IReadOnlyList<string> diagnostics) : ISolutionSource
{
    public async Task<SolutionHandle> AcquireAsync(string? solution, string workingDirectory, CancellationToken ct)
    {
        SolutionHandle real = await WarmWorkspacePool.Source.AcquireAsync(solution, workingDirectory, ct);
        return new SolutionHandle(real.Solution, real.SolutionPath, diagnostics, real, real.WarmFragments);
    }
}
