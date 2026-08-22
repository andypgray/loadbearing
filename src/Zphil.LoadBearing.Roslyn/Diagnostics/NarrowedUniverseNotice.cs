using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn.Diagnostics;

/// <summary>
///     The wording each verb uses to say that a solution filter narrowed the universe it answered over —
///     the human twin of the documents' <c>uncheckedProjects</c> slot.
/// </summary>
/// <remarks>
///     <para>
///         <b>Verbs that report over the universe declare; verbs that read absence as evidence refuse.</b> A
///         narrowed universe is a smaller true answer, so <c>check</c>, <c>status</c>, <c>graph</c> and
///         <c>context</c> answer over it and stamp what was not checked — unlike
///         <see cref="IncompleteModelGate" />, with which these blocks share a composer
///         (<see cref="EvidenceBlock" />) but not a subject: a broken model rather than a small one. But
///         <c>baseline --init</c>, <c>baseline --accept-reductions</c> and <c>render</c> read what is absent
///         as evidence — zero debt to capture, an entry no longer occurring, a card with no home — and write
///         files that outlive the run, so under a narrowing filter they refuse on the gate's own terms
///         (exit 2, nothing written), and the adapter's completeness test skips rather than pass under a name
///         the filtered run cannot vouch for.
///     </para>
///     <para>
///         <b>It names what was not checked, never what the filter did not select.</b> Those are different
///         sets, and only the first is true. Roslyn loads a filter's projects plus their transitive
///         <c>ProjectReference</c> closure, so a filter selecting two of three projects routinely checks all
///         three; a notice built from the filter text would name the third as skipped in a run that checked
///         it. The evidence therefore comes from <c>WorkspaceDiagnostics.UncheckedProjects</c>, which is what
///         actually loaded subtracted from what the solution declares — and which is empty, so nothing
///         prints, whenever a filter narrows nothing.
///     </para>
///     <para>
///         <b>Per-verb tails rather than one parameterized string</b>, for
///         <see cref="IncompleteModelGate" />'s reason: they share a shape, not a template. Each says
///         what <em>that</em> verb's answer is missing — a verdict that cannot be clean for the solution,
///         counts that read low, a survey with projects out of view.
///     </para>
/// </remarks>
internal static class NarrowedUniverseNotice
{
    /// <summary>
    ///     The stamp <c>check</c> writes above its report: a clean verdict over a subset is the exact reading
    ///     this exists to prevent.
    /// </summary>
    /// <param name="filterName">The <c>.slnf</c>'s file name.</param>
    /// <param name="uncheckedProjects">The solution-relative paths that were not checked.</param>
    internal static string CheckStamp(string filterName, IReadOnlyList<string> uncheckedProjects)
    {
        return Block(
            filterName,
            uncheckedProjects,
            "The verdict below covers only the projects that loaded, so a clean result here is not a clean "
            + "solution.");
    }

    /// <summary>
    ///     The stamp <c>status</c> writes above its burndown. An unchecked project declares no types and so
    ///     contributes no violations, which reads as progress rather than as absence.
    /// </summary>
    /// <param name="filterName">The <c>.slnf</c>'s file name.</param>
    /// <param name="uncheckedProjects">The solution-relative paths that were not checked.</param>
    internal static string StatusStamp(string filterName, IReadOnlyList<string> uncheckedProjects)
    {
        return Block(
            filterName,
            uncheckedProjects,
            "The burndown below counts only the projects that loaded: an unchecked project contributes no "
            + "violations, so every count reads low.");
    }

    /// <summary>
    ///     The stamp <c>graph</c> writes above its survey — the verb a stranger runs first on an unfamiliar
    ///     codebase, where a missing project reads as a codebase that simply does not have one.
    /// </summary>
    /// <param name="filterName">The <c>.slnf</c>'s file name.</param>
    /// <param name="uncheckedProjects">The solution-relative paths that were not checked.</param>
    internal static string GraphStamp(string filterName, IReadOnlyList<string> uncheckedProjects)
    {
        return Block(
            filterName,
            uncheckedProjects,
            "The survey below describes only the projects that loaded, so a project or reference edge absent "
            + "from it may simply be out of view.");
    }

    /// <summary>
    ///     The stamp <c>context</c> writes above its answer, beside the caveat
    ///     <see cref="IncompleteModelGate.ContextCaveat" /> writes for a partial model. Stdout is context's
    ///     only channel and it is an MCP tool, so this block is the only way <c>arch_context</c> learns that
    ///     the universe was narrowed.
    /// </summary>
    /// <param name="filterName">The <c>.slnf</c>'s file name.</param>
    /// <param name="uncheckedProjects">The solution-relative paths that were not checked.</param>
    internal static string ContextStamp(string filterName, IReadOnlyList<string> uncheckedProjects)
    {
        return Block(
            filterName,
            uncheckedProjects,
            "The answer below covers only the projects that loaded: a scope card from an unchecked project "
            + "places nowhere, so \"no architecture scope covers\" cannot be trusted for paths under them.");
    }

    /// <summary>
    ///     The stderr lines <c>baseline --init</c> and <c>baseline --accept-reductions</c> emit before exit 2,
    ///     having written nothing — the one place a narrowed universe stops being a smaller true answer. A
    ///     baseline is the team's signature on its debt, and both modes read absence as evidence: <c>--init</c>
    ///     captures "zero debt" for rules whose subjects live in projects the filter left out, and
    ///     <c>--accept-reductions</c> deletes real entries as violations that no longer occur.
    /// </summary>
    /// <param name="filterName">The <c>.slnf</c>'s file name.</param>
    /// <param name="uncheckedProjects">The solution-relative paths that were not checked.</param>
    internal static string BaselineRefusal(string filterName, IReadOnlyList<string> uncheckedProjects)
    {
        return EvidenceBlock.Compose(
            $"error: '{filterName}' narrowed this run — {Subject(uncheckedProjects.Count)}, so no baseline was "
            + "written: a baseline captured through a filter signs off debt in projects it never measured, "
            + "and --accept-reductions deletes real entries as \"no longer occurring\" when the only thing "
            + "that changed is that a project stopped being checked:",
            uncheckedProjects,
            "Run baseline against the solution the filter references rather than through the filter.");
    }

    /// <summary>
    ///     The stderr lines <c>render</c> emits before exit 2, having written nothing. Its output is committed
    ///     context that outlives the run: a card from an unchecked project resolves no directory and is
    ///     dropped rather than written wrong, and <c>--diagram</c> draws a survey missing whole projects.
    /// </summary>
    /// <param name="filterName">The <c>.slnf</c>'s file name.</param>
    /// <param name="uncheckedProjects">The solution-relative paths that were not checked.</param>
    internal static string RenderRefusal(string filterName, IReadOnlyList<string> uncheckedProjects)
    {
        return EvidenceBlock.Compose(
            $"error: '{filterName}' narrowed this run — {Subject(uncheckedProjects.Count)}, so nothing was "
            + "rendered: rendered files are committed context, a card from an unchecked project places "
            + "nowhere and would be dropped from the committed files, and --diagram would draw a survey "
            + "missing whole projects:",
            uncheckedProjects,
            "Run render against the solution the filter references rather than through the filter.");
    }

    /// <summary>
    ///     The <c>Workspace_LoadedCompletely</c> skip reason under a narrowing filter — the adapter's twin of
    ///     the CLI stamps, and a skip rather than a pass for
    ///     <see cref="IncompleteModelGate.AdapterOptedIn" />'s reason: the rule verdicts are real, but a test
    ///     by that name cannot pass while the solution declares projects this run never checked.
    /// </summary>
    /// <param name="filterName">The <c>.slnf</c>'s file name.</param>
    /// <param name="uncheckedProjects">The solution-relative paths that were not checked.</param>
    internal static string AdapterSkip(string filterName, IReadOnlyList<string> uncheckedProjects)
    {
        return Block(
            filterName,
            uncheckedProjects,
            "Rule verdicts come from the projects that loaded, but a test by this name cannot pass while "
            + "declared projects went unchecked; run the solution the filter references for the whole "
            + "answer.");
    }

    /// <summary>
    ///     The reason a rule the filter emptied reports, on every surface a report reaches: one line, no
    ///     evidence block, because the projects themselves ride the stamp above the report (or the adapter's
    ///     <see cref="AdapterSkip" />) — one place to read them, not one copy per rule, which is
    ///     <see cref="IncompleteModelGate.AdapterSkipReason" />'s reasoning for the same shape.
    /// </summary>
    /// <remarks>
    ///     It ends by denying the reading that makes a narrowed run dangerous. A skipped rule prints beside
    ///     passing ones and gates nothing, so the sentence has to say outright that this is not the rule
    ///     holding — only the run declining to claim either way.
    /// </remarks>
    /// <param name="filterName">The <c>.slnf</c>'s file name.</param>
    /// <param name="uncheckedProjectCount">How many declared projects this run did not check.</param>
    internal static string RuleSkipReason(string filterName, int uncheckedProjectCount)
    {
        return $"'{filterName}' narrowed this run: {Subject(uncheckedProjectCount)}, and this rule's subject "
               + "matched no type in the projects that loaded — so it was not measured, and this is not a "
               + "clean result for it.";
    }

    /// <summary>
    ///     The unchecked projects as a notice shows them — solution-relative and forward-slashed, like every
    ///     path in the documents beside them, so a machine path never lands in output a golden pins.
    /// </summary>
    /// <param name="projects">The absolute <c>.csproj</c> paths.</param>
    /// <param name="solutionDirectory">The directory they are shown relative to.</param>
    internal static IReadOnlyList<string> Relative(IReadOnlyList<string> projects, string solutionDirectory)
    {
        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        return projects
            .Select(relativizer.Relative)
            .ToList();
    }

    // The stamps' own lede — fixed, because a stamp scopes an answer that still follows and has nothing else
    // to say first. The refusals write their own, since what they refuse is the point of the sentence.
    private static string Block(string filterName, IReadOnlyList<string> uncheckedProjects, string tail)
    {
        var lede = $"'{filterName}' narrowed this run: {Subject(uncheckedProjects.Count)}.";
        return EvidenceBlock.Compose(lede, uncheckedProjects, tail);
    }

    // The count as a clause every lede can take, in both numbers: a block that read "1 projects ... were not
    // checked" is the sentence a reader stops trusting. Taken as a count rather than the list, because the
    // per-rule skip reason names the number without ever showing the paths. Noun and copula both come from
    // the one inflection owner — the same clause is written in three other places, and English agreeing with
    // itself in one of them and not the next is precisely what a single owner exists to prevent.
    private static string Subject(int uncheckedProjectCount)
    {
        string noun = Plurals.Noun(uncheckedProjectCount, "project");
        string copula = Plurals.PastVerb(uncheckedProjectCount);

        return $"{uncheckedProjectCount} {noun} the solution declares {copula} not checked";
    }
}
