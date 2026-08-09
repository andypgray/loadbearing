using Zphil.LoadBearing.Cli.Rendering;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The one answer to a partially-loaded workspace, shared by every verb that consumes the model.
/// </summary>
/// <remarks>
///     <para>
///         <b>The answer, stated once.</b> A partially-loaded workspace is never silently treated as the
///         model. Extraction treats unresolved symbols as legal input and never crashes on them. Every verb
///         that consumes the model either fails closed on <c>check</c>'s terms — exit 2 / MCP error, opt-out
///         <c>--allow-workspace-diagnostics</c> — or carries the diagnostics visibly in its output document.
///         NuGet-audit advisories never gate. Where a verb refuses <em>before</em> producing its document,
///         the refusal is the error channel (CLI stderr + exit 2, MCP <c>IsError</c>); where it gates
///         <em>after</em> producing one, the verdict rides in the document (as SARIF already does).
///     </para>
///     <para>
///         <b>What gates.</b> Strictly the workspace-load failures
///         (<see cref="CodebaseSource.Diagnostics" />), never the advisory merge notes
///         (<see cref="CodebaseSource.MergeNotes" />, which ride a separate stream by construction) and
///         never the NuGetAudit advisories (NU19xx) that do share the load-failure stream — an advisory's
///         publication timing is an external, time-varying input, not a statement that the model failed to
///         build, so <see cref="NuGetAuditDiagnostics" /> carves the family out of the gate input while it
///         still renders everywhere.
///     </para>
///     <para>
///         <b>Why per-verb constants rather than one parameterized string.</b> The messages share a shape,
///         not a template: each names what <em>that</em> verb cannot do and what is at stake if it did it
///         anyway. Keeping them as separate literals is also what freezes <see cref="CheckMessage" />'s
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
    ///     Whether the gate fires: at least one workspace-load failure that is not a NuGetAudit advisory, and
    ///     no opt-out. The verbs that render before gating call this twice-over — once for the verdict they
    ///     stamp into their document, once for the exit code — so it stays a pure predicate.
    /// </summary>
    /// <param name="diagnostics">
    ///     The workspace-load diagnostics, and only those: never merge notes, and never a
    ///     <see cref="WorkspaceDiagnosticsRenderer.Compose" />d list. Its trailing MSBuild-selection note is
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
    ///     The <c>graph</c> refusal, thrown as a <see cref="Roslyn.UserErrorException" /> before extraction
    ///     rather than written to a stream — so the identical text reaches CLI stderr (exit 2, via
    ///     <see cref="CliErrorMapper" />) and an MCP <c>arch_graph</c> error result (via
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
        lines.Add("  " + WorkspaceDiagnosticsRenderer.MsBuildNote());
        lines.Add(
            "Restore and build the solution first (dotnet build), then retry. To survey the partial model as it "
            + "loaded, pass --allow-workspace-diagnostics (CLI) or allowWorkspaceDiagnostics: true (arch_graph).");
        return string.Join("\n", lines);
    }
}
