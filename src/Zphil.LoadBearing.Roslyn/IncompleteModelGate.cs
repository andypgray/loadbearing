using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     The one answer to a partially-loaded workspace, shared by every surface that consumes the model —
///     the CLI verbs, the MCP tools, and the xUnit adapter.
/// </summary>
/// <remarks>
///     <para>
///         <b>The answer, stated once.</b> A partially-loaded workspace is never silently treated as the
///         model. Extraction treats unresolved symbols as legal input and never crashes on them. Every
///         surface that consumes the model either fails closed on <c>check</c>'s terms — exit 2 / MCP error
///         / a named failing test, opt-out <c>--allow-workspace-diagnostics</c>
///         (<c>AllowWorkspaceDiagnostics</c> in the adapter) — or carries the diagnostics visibly on its own
///         answering channel. NuGet-audit advisories never gate. Where a surface refuses <em>before</em>
///         producing its answer, the refusal is the error channel (CLI stderr + exit 2, MCP <c>IsError</c>,
///         the adapter's <c>Workspace_LoadedCompletely</c> failure with every rule case skipped); where it
///         answers anyway, the diagnostics ride the answer (the JSON documents'
///         <c>workspaceDiagnostics</c> + <c>modelIncomplete</c>, <c>explain</c>'s stderr warnings,
///         <c>context</c>'s leading caveat block).
///     </para>
///     <para>
///         <b>What gates.</b> Strictly the workspace-load failures
///         (<c>CodebaseSource.Diagnostics</c>), never the advisory merge notes
///         (<c>CodebaseSource.MergeNotes</c>, which ride a separate stream by construction) and
///         never the NuGetAudit advisories (NU19xx) that do share the load-failure stream — an advisory's
///         publication timing is an external, time-varying input, not a statement that the model failed to
///         build, so <see cref="NuGetAuditDiagnostics" /> carves the family out of the gate input while it
///         still renders everywhere.
///     </para>
///     <para>
///         <b>Why per-surface constants rather than one parameterized string.</b> The messages share a
///         shape, not a template: each names what <em>that</em> surface cannot do and what is at stake if it
///         did it anyway. Keeping them as separate literals is also what freezes <see cref="CheckMessage" />'s
///         bytes, which the check gate's pinned tests duplicate verbatim.
///     </para>
/// </remarks>
internal static class IncompleteModelGate
{
    /// <summary>
    ///     The stderr line <c>check</c> emits before exit 2. Kept as one line, printed after the per-project
    ///     load warnings it refers to, and naming the opt-out flag.
    /// </summary>
    internal const string CheckMessage =
        "error: the model is incomplete — one or more projects failed to load (see the warnings above), so check "
        + "cannot pass. Pass --allow-workspace-diagnostics to check against the partial model anyway.";

    /// <summary>
    ///     The stderr line <c>baseline</c> emits before exit 2, having written nothing. A baseline is the
    ///     team's signature on its debt: captured from a partial model it grandfathers "zero debt" for rules
    ///     that were never measured, and <c>--accept-reductions</c> deletes real entries as "no longer
    ///     occurring" when the only thing that changed is that a project stopped loading.
    /// </summary>
    internal const string BaselineMessage =
        "error: the model is incomplete — one or more projects failed to load (see the warnings above), so no "
        + "baseline was written: a baseline captured from a partial model signs off debt that was never measured. "
        + "Pass --allow-workspace-diagnostics to baseline against the partial model anyway.";

    /// <summary>
    ///     The stderr line <c>status</c> emits before exit 2, after rendering the burndown it does have.
    ///     Unloaded projects declare no types, so every count in that burndown reads low.
    /// </summary>
    internal const string StatusMessage =
        "error: the model is incomplete — one or more projects failed to load (see the warnings above), so status "
        + "cannot report the burndown: unloaded projects contribute no violations, so every count reads low. "
        + "Pass --allow-workspace-diagnostics to report against the partial model anyway.";

    /// <summary>
    ///     The stderr line <c>render</c> emits before exit 2, having written nothing. Rendered files are
    ///     committed context: a card whose project failed to load resolves no directory and is silently
    ///     dropped rather than written wrong, and <c>--diagram</c> draws the very survey <c>graph</c>
    ///     refuses to print from a partial model.
    /// </summary>
    internal const string RenderMessage =
        "error: the model is incomplete — one or more projects failed to load (see the warnings above), so nothing "
        + "was rendered: a card whose project failed to load cannot be placed and would be dropped from the "
        + "committed files, and --diagram would draw a survey missing whole projects. "
        + "Pass --allow-workspace-diagnostics to render from the partial model anyway.";

    /// <summary>
    ///     Whether the gate fires: at least one workspace-load failure that is not a NuGetAudit advisory, and
    ///     no opt-out. The verbs that render before gating call this twice-over — once for the verdict they
    ///     stamp into their document, once for the exit code — so it stays a pure predicate.
    /// </summary>
    /// <param name="diagnostics">
    ///     The workspace-load diagnostics, and only those: never merge notes, and never a
    ///     <c>WorkspaceDiagnosticsRenderer.Compose</c>d list. Its trailing MSBuild-selection note is
    ///     not an advisory, so <see cref="IsIncomplete" /> would read it as a load failure and gate every run.
    /// </param>
    /// <param name="allowWorkspaceDiagnostics">Whether the operator opted into the partial model.</param>
    internal static bool Gates(IReadOnlyList<string> diagnostics, bool allowWorkspaceDiagnostics)
    {
        return !allowWorkspaceDiagnostics && IsIncomplete(diagnostics);
    }

    /// <summary>
    ///     Whether the model is incomplete — a load failure that is not a NuGetAudit advisory — independently
    ///     of whether the operator opted in. This is the fact the JSON documents carry: the opt-out changes
    ///     the exit code, never the truth about the model.
    /// </summary>
    internal static bool IsIncomplete(IReadOnlyList<string> diagnostics)
    {
        return diagnostics.Any(diagnostic => !NuGetAuditDiagnostics.IsAudit(diagnostic));
    }

    /// <summary>
    ///     The <c>graph</c> refusal, thrown as a <see cref="UserErrorException" /> before extraction
    ///     rather than written to a stream — so the identical text reaches CLI stderr (exit 2, via
    ///     <c>CliErrorMapper</c>) and an MCP <c>arch_graph</c> error result (via
    ///     <c>GlobalCallToolFilter</c>).
    /// </summary>
    /// <remarks>
    ///     It carries the diagnostics inline rather than pointing at warnings printed above, because on the
    ///     MCP surface there is nothing above: <c>arch_graph</c> discards its error writer, so a refusal that
    ///     said "see the warnings" would name evidence the caller cannot reach. <c>graph</c> is also the
    ///     entry point that needs no spec — a stranger's first command on an unfamiliar codebase — so the
    ///     refusal names the fix in both dialects rather than assuming which surface asked.
    /// </remarks>
    internal static string GraphRefusal(IReadOnlyList<string> diagnostics)
    {
        var lines = new List<string>
        {
            "the model is incomplete — one or more projects failed to load, so graph cannot survey the codebase:"
        };
        lines.AddRange(diagnostics.Select(diagnostic => "  " + diagnostic));
        lines.Add("  " + MsBuildBootstrap.SelectionNote());
        lines.Add(
            "Restore and build the solution first (dotnet build), then retry. To survey the partial model as it "
            + "loaded, pass --allow-workspace-diagnostics (CLI) or allowWorkspaceDiagnostics: true (arch_graph).");
        return string.Join("\n", lines);
    }

    /// <summary>
    ///     The caveat block <c>context</c> writes above its answer when the model is incomplete. Context
    ///     never gates — it is a lookup an agent runs mid-edit, and a partial answer beats none — but a
    ///     partial load is exactly the state in which "no architecture scope covers this path" can be a
    ///     false all-clear: a card whose project failed to load resolves no directory and places nowhere.
    ///     Stdout is context's only channel (no CLI twin, no <c>--json</c>), so the caveat rides the body,
    ///     diagnostics inline, ahead of whatever answer the partial model still supports.
    /// </summary>
    internal static string ContextCaveat(IReadOnlyList<string> diagnostics)
    {
        var lines = new List<string>
        {
            "caveat: the model is incomplete — one or more projects failed to load, so scope cards from the "
            + "unloaded projects cannot be placed and \"no architecture scope covers\" cannot be trusted for "
            + "paths under them:"
        };
        lines.AddRange(diagnostics.Select(diagnostic => "  " + diagnostic));
        lines.Add("  " + MsBuildBootstrap.SelectionNote());
        lines.Add("Restore and build the solution first (dotnet build), then retry for a whole answer.");
        return string.Join("\n", lines);
    }

    /// <summary>
    ///     The <c>Workspace_LoadedCompletely</c> failure body — the CLI gate transposed to a test report:
    ///     <c>check</c> gates and emits no verdict, so the adapter emits no rule verdicts. It carries the
    ///     diagnostics inline for <see cref="GraphRefusal" />'s exact reason: in a test report there is
    ///     nothing above to point at.
    /// </summary>
    internal static string AdapterRefusal(IReadOnlyList<string> diagnostics)
    {
        var lines = new List<string>
        {
            "the model is incomplete — one or more projects failed to load, so every rule test was skipped:"
        };
        lines.AddRange(diagnostics.Select(diagnostic => "  " + diagnostic));
        lines.Add("  " + MsBuildBootstrap.SelectionNote());
        lines.Add(
            "A rule whose subject lives in an unloaded project selects nothing, and an empty subject passes — a "
            + "green run against a partial model would sign a verdict that was never reached. Restore and build "
            + "the solution first (dotnet build), then retry. To check the partial model as it loaded, override "
            + "AllowWorkspaceDiagnostics to true on the test class.");
        return string.Join("\n", lines);
    }

    /// <summary>
    ///     The <c>Workspace_LoadedCompletely</c> skip reason under <c>AllowWorkspaceDiagnostics</c>: the rule
    ///     verdicts are live again, but a test by that name cannot pass while the load failures it is named
    ///     for are real — so it skips, and the diagnostics ride the skip reason.
    /// </summary>
    internal static string AdapterOptedIn(IReadOnlyList<string> diagnostics)
    {
        var lines = new List<string>
        {
            "AllowWorkspaceDiagnostics is true: rule verdicts come from the partial model that loaded, despite:"
        };
        lines.AddRange(diagnostics.Select(diagnostic => "  " + diagnostic));
        return string.Join("\n", lines);
    }

    /// <summary>
    ///     The per-rule skip reason when the gate fires: short and constant, because the diagnostics
    ///     themselves ride <c>Workspace_LoadedCompletely</c>'s failure — one place to read them, not one copy
    ///     per rule.
    /// </summary>
    internal const string AdapterSkipReason =
        "the workspace did not load completely, so no verdict was reached; see Workspace_LoadedCompletely for "
        + "the load failures.";
}
