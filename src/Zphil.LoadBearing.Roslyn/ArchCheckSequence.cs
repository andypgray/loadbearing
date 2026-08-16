using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Roslyn.Baselines;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     The check sequence, in one place: load the ratcheted baselines, resolve the optional diff context,
///     extract the codebase, evaluate the rules. Both surfaces that check a spec run this — the CLI's
///     <c>check</c>/<c>status</c> pipeline and the xUnit adapter — so the ordering they both depend on is
///     enforced here rather than restated in each and enforced in neither.
/// </summary>
/// <remarks>
///     <para>
///         <b>Fail-fast precedes extraction.</b> A tampered baseline file, and a <c>--diff-base</c> ref that
///         does not resolve, must fail the run <em>before</em> the Roslyn walk it would otherwise pay for.
///         Both arrive as work this type schedules rather than as work a caller has already done: extraction
///         is a delegate so the ordering survives whatever a caller's extraction costs (a cache-hit fragment
///         merge, a warm session's incremental re-walk, or a cold workspace load the delegate performs
///         itself), and the diff is a delegate for the same reason — it shells out to git, which is a
///         precondition to check, not a cost to pay twice.
///     </para>
///     <para>
///         It lives in the extraction assembly because that is the one project both the CLI host and the
///         adapter reference — the route <see cref="IncompleteModelGate" /> took for the same reason. What
///         stays with each caller is the part that is genuinely theirs: the CLI owns the persisted cache and
///         git, the adapter owns the cold one-shot workspace.
///     </para>
/// </remarks>
internal static class ArchCheckSequence
{
    /// <summary>
    ///     Runs the sequence and returns the report: the baselines for <paramref name="model" /> load from
    ///     <paramref name="solutionDirectory" />, then <paramref name="resolveDiff" /> (when given) produces
    ///     the changed-file context, then <paramref name="extract" /> produces the codebase and the projects
    ///     it left unchecked, then <paramref name="rules" /> are evaluated against it — over the whole
    ///     solution, or over the narrowed universe a <c>.slnf</c> left.
    /// </summary>
    /// <param name="model">The finalized architecture model whose baseline files are loaded.</param>
    /// <param name="rules">The rules to evaluate — the whole model's, or a narrowed selection.</param>
    /// <param name="solutionPath">
    ///     The solution — or the <c>.slnf</c> over one — this run was pointed at. Its file name is what a
    ///     narrowing skip names, so the operator reads back the file they passed.
    /// </param>
    /// <param name="solutionDirectory">The directory the rules' baseline paths resolve against.</param>
    /// <param name="extract">
    ///     Produces the codebase to check and the declared projects this run did not. Invoked only once the
    ///     baselines are in hand.
    /// </param>
    /// <param name="resolveDiff">
    ///     Produces the changed-file context a Quarantine tripwire warns from, or <see langword="null" /> when
    ///     the run has no diff base and its tripwires skip.
    /// </param>
    /// <param name="ct">Cancellation token, flowed into both delegates.</param>
    /// <returns>The aggregate report over <paramref name="rules" />.</returns>
    internal static async Task<CheckReport> ExecuteAsync(
        ArchitectureModel model,
        IReadOnlyList<ArchRule> rules,
        string solutionPath,
        string solutionDirectory,
        Func<CancellationToken, Task<ExtractedCodebase>> extract,
        Func<CancellationToken, Task<DiffContext>>? resolveDiff,
        CancellationToken ct)
    {
        BaselineIndex baselines = BaselineStore.LoadForModel(model, solutionDirectory);

        DiffContext? diff = resolveDiff is null ? null : await resolveDiff(ct);

        ExtractedCodebase extracted = await extract(ct);
        NarrowedUniverse? narrowing = Narrowing(solutionPath, extracted.UncheckedProjects);

        return ArchChecker.Check(rules, extracted.Codebase, baselines, diff, narrowing);
    }

    // Null unless this run actually checked less than the solution declares — measured from the load, never
    // read off the filter text, for the reason NarrowedUniverseNotice's remarks give. The prose is composed
    // there too, beside the stamp that carries the projects this reason only counts.
    private static NarrowedUniverse? Narrowing(string solutionPath, IReadOnlyList<string> uncheckedProjects)
    {
        if (uncheckedProjects.Count == 0) return null;

        string filterName = Path.GetFileName(solutionPath);
        string reason = NarrowedUniverseNotice.RuleSkipReason(filterName, uncheckedProjects.Count);
        return new NarrowedUniverse(reason);
    }
}
