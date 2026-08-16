using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     An <see cref="ISolutionSource" /> that wraps a real load, then re-wraps the handle with synthetic
///     workspace-load diagnostics, synthetic failed projects, synthetic restore-failed ones and/or synthetic
///     unchecked ones — the one way to drive the incomplete-model gate, and the narrowed-universe slots, over
///     a fixture that loads perfectly well. Shared by the gate tests of every verb that has one, so
///     <c>check</c>, <c>baseline</c>, <c>status</c> and <c>graph</c> are exercised against the same injected
///     input rather than each inventing its own.
/// </summary>
/// <remarks>
///     <para>
///         <b>The four inputs are separate on purpose, because the product separates them.</b> Diagnostics
///         render and never gate; failed projects and restore-failed projects each gate, with their own
///         wording and their own remedy, and are what every refusal names; unchecked projects scope the answer
///         and never gate at all. A caller that wants a refusal injects a failed or restore-failed project, a
///         caller that wants a rendered-but-harmless warning injects a diagnostic, a caller that wants a
///         narrowed universe injects an unchecked project, and a caller testing that they do not leak into
///         each other injects one without the others — which is the shape both localized-message defects took
///         in the field.
///     </para>
///     <para>
///         <b>Why the restore cause is injected rather than restored.</b> A real broken restore costs a live
///         SDK, a folder feed and a private package root, which is what
///         <see cref="Zphil.LoadBearing.Tests.Cli.RestoreFailureSilentEdgeE2ETests" /> pays once for the
///         end-to-end fact. Every wording, exit code and document slot downstream of the gate is a function of
///         the list alone, so paying that cost again per surface would buy nothing.
///     </para>
///     <para>
///         The inner source is the shared warm pool: nothing here asserts on whether a workspace was opened,
///         only on what the gate does with what rides the handle. The real handle rides as the owned
///         disposable so the ownership chain still holds — it is the pool's, so disposing it is a no-op.
///         Injected values never reach a real workspace, so the pooled session stays clean for the next
///         test; a genuinely partial load belongs in
///         <see cref="Zphil.LoadBearing.Tests.Cli.PartialLoadWorkspaceE2ETests" />, which drives a cold load
///         over a fixture the loader really does fail on.
///     </para>
/// </remarks>
internal sealed class DiagnosticInjectingSolutionSource(
    IReadOnlyList<string> diagnostics,
    IReadOnlyList<string>? failedProjects = null,
    IReadOnlyList<string>? uncheckedProjects = null,
    IReadOnlyList<string>? restoreFailedProjects = null) : ISolutionSource
{
    public async Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        SolutionHandle real = await WarmWorkspacePool.Source.AcquireAsync(solutionPath, ct);
        var injected = new WorkspaceDiagnostics(
            diagnostics, [], failedProjects ?? [], uncheckedProjects ?? [], restoreFailedProjects ?? []);
        // Every warm affordance the inner handle carried is forwarded, not just the ones a caller happens to
        // exercise today: this double re-wraps a real handle, so anything it drops silently sends the run
        // down the cold path and quietly changes what the gate is being tested against.
        return new SolutionHandle(
            real.Solution, real.SolutionPath, injected, real, real.WarmCodebase, real.WarmSpecResolution,
            real.TargetFrameworks);
    }
}
