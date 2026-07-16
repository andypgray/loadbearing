using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Diff;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Baselines;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The shared check core over a <see cref="CodebaseSource" />, reused by <c>check</c> and <c>status</c>:
///     load the ratcheted baselines <em>before</em> extraction (so a tampered file fails fast, before the
///     expensive Roslyn walk), resolve the optional <c>--diff-base</c> diff (a bad ref also fails fast),
///     extract the codebase excluding the spec project, and evaluate. The two commands differ only in how
///     they render the resulting <see cref="CheckReport" /> and their exit codes — <c>check</c> gates,
///     <c>status</c> reports (and passes no diff base, so its tripwires skip).
/// </summary>
/// <remarks>
///     The baseline load and diff resolution deliberately precede <see cref="CodebaseSource.ExtractAsync" />:
///     a tampered baseline or a bad <c>--diff-base</c> must fail fast before any extraction runs, whether that
///     extraction is a cheap cache-hit merge or a full cold workspace walk.
/// </remarks>
internal static class CheckPipeline
{
    public static async Task<CheckReport> ExecuteAsync(CodebaseSource source, string? diffBase, CancellationToken ct)
    {
        BaselineIndex baselines = BaselineStore.LoadForModel(source.Model, source.SolutionDirectory);

        // Resolve the diff before extraction so a bad ref (or missing git) fails fast, mirroring the
        // baseline-before-extraction ordering.
        DiffContext? diff = diffBase is null ? null : GitChangedFiles.Resolve(diffBase, source.SolutionDirectory);

        CodebaseModel codebase = await source.ExtractAsync(source.Resolution.ExcludeProjectName, ct);

        return ArchChecker.Check(source.Model, codebase, baselines, diff);
    }
}