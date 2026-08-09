using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Diff;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Baselines;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The shared check core over a <see cref="CodebaseSource" />, reused by <c>check</c> and <c>status</c>:
///     load the ratcheted baselines <em>before</em> extraction (so a tampered file fails fast, before the
///     expensive Roslyn walk), resolve the optional <c>--diff-base</c> diff (a bad ref also fails fast),
///     extract the codebase excluding the spec project and its private plumbing, and evaluate the rules the
///     caller selected. The two commands differ only in how they render the resulting
///     <see cref="CheckReport" /> and their exit codes — <c>check</c> gates, <c>status</c> reports (and
///     passes no diff base, so its tripwires skip).
/// </summary>
/// <remarks>
///     The baseline load and diff resolution deliberately precede <see cref="CodebaseSource.ExtractAsync" />:
///     a tampered baseline or a bad <c>--diff-base</c> must fail fast before any extraction runs, whether that
///     extraction is a cheap cache-hit merge or a full cold workspace walk. <see cref="SelectRules" /> is
///     earlier still, and separated from <see cref="ExecuteAsync" /> for that reason — rule IDs are a
///     property of the spec model, which is already in hand, so an unmatched filter must never cost a
///     codebase walk to refuse.
/// </remarks>
internal static class CheckPipeline
{
    /// <summary>
    ///     The rules a run evaluates: every rule for an empty glob list, otherwise the matches of
    ///     <paramref name="ruleIdGlobs" />. A filter that matches nothing refuses with the available IDs
    ///     rather than checking an empty model, which would exit 0 and read as a clean solution.
    /// </summary>
    public static IReadOnlyList<ArchRule> SelectRules(ArchitectureModel model, IReadOnlyList<string> ruleIdGlobs)
    {
        var selected = ArchChecker.SelectRules(model, ruleIdGlobs);
        if (ruleIdGlobs.Count > 0 && selected.Count == 0)
            throw new UserErrorException(UnmatchedRulesMessage(ruleIdGlobs, model));

        return selected;
    }

    public static async Task<CheckReport> ExecuteAsync(
        CodebaseSource source, string? diffBase, IReadOnlyList<ArchRule> rules, CancellationToken ct)
    {
        BaselineIndex baselines = BaselineStore.LoadForModel(source.Model, source.SolutionDirectory);

        // Resolve the diff before extraction so a bad ref (or missing git) fails fast, mirroring the
        // baseline-before-extraction ordering.
        DiffContext? diff = diffBase is null
            ? null
            : await GitChangedFiles.ResolveAsync(diffBase, source.SolutionDirectory, ct);

        CodebaseModel codebase = await source.ExtractAsync(source.Resolution.ExcludeProjectNames, ct);

        return ArchChecker.Check(rules, codebase, baselines, diff);
    }

    // The unmatched-filter refusal, worded like explain's unknown-rule refusal and graph's unmatched-project
    // one: name what did not match, then list what was available, so the next command is one edit away.
    private static string UnmatchedRulesMessage(IReadOnlyList<string> ruleIdGlobs, ArchitectureModel model)
    {
        var ids = model.Rules.Select(rule => rule.Id).OrderBy(id => id, StringComparer.Ordinal);
        return $"No rule matched '{string.Join(";", ruleIdGlobs)}'. Available rule IDs:\n  "
               + string.Join("\n  ", ids);
    }
}
