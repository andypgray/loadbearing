using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Diff;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Baselines;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The shared check core over an open workspace, reused by <c>check</c> and <c>status</c>: load the
///     ratcheted baselines <em>before</em> extraction (so a tampered file fails fast, before the
///     expensive Roslyn walk), resolve the optional <c>--diff-base</c> diff (a bad ref also fails fast),
///     extract the codebase excluding the spec project, and evaluate. The two commands differ only in how
///     they render the resulting <see cref="CheckReport" /> and their exit codes — <c>check</c> gates,
///     <c>status</c> reports (and passes no diff base, so its tripwires skip).
/// </summary>
internal static class CheckPipeline
{
    public static async Task<CheckReport> ExecuteAsync(WorkspaceModel workspace, string? diffBase, CancellationToken ct)
    {
        BaselineIndex baselines = BaselineStore.LoadForModel(workspace.Model, workspace.SolutionDirectory);

        // Resolve the diff before extraction so a bad ref (or missing git) fails fast, mirroring the
        // baseline-before-extraction ordering.
        DiffContext? diff = diffBase is null ? null : GitChangedFiles.Resolve(diffBase, workspace.SolutionDirectory);

        IReadOnlyCollection<string>? exclude = workspace.Resolution.ExcludeProjectName is { } name ? [name] : null;
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(workspace.Solution, exclude, ct);

        return ArchChecker.Check(workspace.Model, codebase, baselines, diff);
    }
}