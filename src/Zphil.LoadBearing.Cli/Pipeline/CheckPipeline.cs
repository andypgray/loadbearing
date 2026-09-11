using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Diff;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Checking;
using Zphil.LoadBearing.Roslyn.Extraction;

namespace Zphil.LoadBearing.Cli.Pipeline;

/// <summary>
///     The CLI's half of the check core over a <see cref="CodebaseSource" />, reused by <c>check</c> and
///     <c>status</c>: pick the rules, name the two things only the host can supply — how to extract this
///     run's codebase, and how to resolve its <c>--diff-base</c> — and hand them to
///     <see cref="ArchCheckSequence" />, which owns the order they run in. The two commands differ only in
///     how they render the resulting <see cref="CheckReport" /> and their exit codes — <c>check</c> gates,
///     <c>status</c> reports (and passes no diff base, so its tripwires skip).
/// </summary>
/// <remarks>
///     Both fail-fast preconditions — a tampered baseline, an unresolvable <c>--diff-base</c> — precede
///     <see cref="CodebaseSource.ExtractAsync" />, which is why extraction goes down as a delegate rather
///     than as an already-extracted model: the ordering is <see cref="ArchCheckSequence" />'s to hold, and it
///     holds whether the extraction is a cheap cache-hit merge or a full cold workspace walk.
///     <see cref="SelectRules" /> is earlier still, and separated from <see cref="ExecuteAsync" /> for that
///     reason — rule IDs are a property of the spec model, which is already in hand, so an unmatched filter
///     must never cost a codebase walk to refuse.
/// </remarks>
internal static class CheckPipeline
{
    /// <summary>
    ///     The rules a run evaluates: every rule for an empty glob list, otherwise the matches of
    ///     <paramref name="ruleIdGlobs" />. A filter that matches nothing refuses with the available IDs
    ///     rather than checking an empty model, which would exit 0 and read as a clean solution — or, where
    ///     the filter is a JSON array written as text, names that shape instead (<see cref="Refusals" />).
    /// </summary>
    public static IReadOnlyList<ArchRule> SelectRules(ArchitectureModel model, IReadOnlyList<string> ruleIdGlobs)
    {
        IReadOnlyList<ArchRule> selected = ArchChecker.SelectRules(model, ruleIdGlobs);
        if (ruleIdGlobs.Count > 0 && selected.Count == 0)
            throw new UserErrorException(UnmatchedRulesMessage(ruleIdGlobs, model));

        return selected;
    }

    public static Task<CheckReport> ExecuteAsync(
        CodebaseSource source, string? diffBase, IReadOnlyList<ArchRule> rules, CancellationToken ct)
    {
        Func<CancellationToken, Task<ExtractedCodebase>> extract = async token =>
        {
            CodebaseModel codebase = await source.ExtractAsync(source.Resolution.ExcludeProjectNames, token);
            return new ExtractedCodebase(codebase, source.Diagnostics);
        };

        Func<CancellationToken, Task<DiffContext>>? resolveDiff = diffBase is null
            ? null
            : token => GitChangedFiles.ResolveAsync(diffBase, source.SolutionDirectory, token);

        return ArchCheckSequence.ExecuteAsync(
            source.Model, rules, source.SolutionPath, source.SolutionDirectory, extract, resolveDiff, ct);
    }

    // The unmatched-filter refusal, in the shared shape explain's unknown-rule refusal and graph's
    // unmatched-project one also take — except when the filter is a JSON array a client wrote into the
    // string parameter, which is answered on its shape instead of with a roster whose entries the reader
    // can already read off their own brackets.
    private static string UnmatchedRulesMessage(IReadOnlyList<string> ruleIdGlobs, ArchitectureModel model)
    {
        string written = string.Join(";", ruleIdGlobs);
        var lead = $"No rule matched '{written}'";

        return Refusals.StringifiedArrayRefusal(written, lead, Refusals.GlobListAdvice)
               ?? Refusals.RuleNotFound(lead, model);
    }
}
