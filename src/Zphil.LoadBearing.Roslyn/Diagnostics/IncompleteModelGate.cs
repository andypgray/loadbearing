using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn.Diagnostics;

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
///         answering channel. Where a surface refuses <em>before</em> producing its answer, the refusal is
///         the error channel (CLI stderr + exit 2, MCP <c>IsError</c>, the adapter's
///         <c>Workspace_LoadedCompletely</c> failure with every rule case skipped); where it answers anyway,
///         the diagnostics ride the answer (the JSON documents' <c>workspaceDiagnostics</c> +
///         <c>modelIncomplete</c> + <c>failedProjects</c> + <c>restoreFailedProjects</c>, <c>explain</c>'s
///         stderr warnings, <c>context</c>'s leading caveat block).
///     </para>
///     <para>
///         <b>What gates.</b> Strictly two sets of projects: the ones that failed to load
///         (<see cref="WorkspaceDiagnostics.FailedProjects" />), read off the loaded solution's structure by
///         <see cref="ProjectLoadFailures" />, and the ones whose NuGet packages are not in the model
///         (<see cref="WorkspaceDiagnostics.RestoreFailedProjects" />), read off their assets files — present
///         and recording an error, or absent where an SDK-style project would have written one — by
///         <see cref="RestoreFailures" />. Never the diagnostics: MSBuild severity does not survive
///         Roslyn's project-load reporting, so <see cref="WorkspaceDiagnostics.LoadFailures" /> carries fatal
///         evaluation errors and ordinary restore warnings in one indistinguishable stream, and a gate that
///         read it refused solutions whose rules all passed — and refused in German what it let through in
///         English. The decision itself lives on <see cref="WorkspaceDiagnostics.Gates" />, where the
///         gating and the rendered streams cannot be swapped; what stays here is the wording each surface
///         uses once it has fired.
///     </para>
///     <para>
///         <b>Two causes, and the order they are told in.</b> A surface with both writes the load block first
///         and the restore block second: a project that never loaded is more fundamentally broken than one
///         that loaded without its packages, and the two ask for different repairs (<c>dotnet build</c> versus
///         <c>dotnet restore</c>). It is the same precedence the narrowing notice already keeps — a broken
///         model outranks a small one — carried one step further. A run with only one cause writes only that
///         block, byte-for-byte as this class wrote it when the load failure was the only cause there was.
///     </para>
///     <para>
///         <b>Every message names the projects.</b> The gate's evidence is inherently a set of paths rather
///         than a set of sentences, so each refusal lists them — which is also the one thing a reader can act
///         on directly. The diagnostics that explain <em>why</em> each failed still render on whatever
///         channels that surface has; the four CLI verbs below print them immediately above their refusal,
///         which is why those four point at the warnings rather than repeating them. The restore cause points
///         at them <em>conditionally</em>, and the condition is the whole reason the wording is careful: the
///         SDK replays NuGet's own logs on every later build, so the code and wording behind a restore that
///         ran and failed reach those channels even though this tool never restores — while a restore that
///         never ran produced no logs for anything to replay, and a refusal promising warnings that are not
///         there would send the reader looking for them. Either way no refusal quotes a NuGet code it would
///         have had to parse for.
///     </para>
///     <para>
///         <b>Why per-surface messages rather than one parameterized string.</b> They share a shape, not a
///         template: each names what <em>that</em> surface cannot do and what is at stake if it did it
///         anyway. Only the assembly of the evidence block is shared, and that was never the part that
///         varied.
///     </para>
/// </remarks>
internal static class IncompleteModelGate
{
    /// <summary>
    ///     The per-rule skip reason when the gate fires: short and constant, because the failed projects
    ///     themselves ride <c>Workspace_LoadedCompletely</c>'s failure — one place to read them, not one copy
    ///     per rule.
    /// </summary>
    internal const string AdapterSkipReason =
        "the workspace did not load completely, so no verdict was reached; see Workspace_LoadedCompletely for "
        + "the projects that failed to load or whose NuGet packages did not resolve.";

    /// <summary>
    ///     The stderr lines <c>check</c> emits before exit 2, printed after the per-project load warnings
    ///     they refer to, and naming the opt-out flag.
    /// </summary>
    internal static string CheckMessage(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            false,
            $"error: the model is incomplete — {Failed(diagnostics)} failed to load, so check cannot pass:",
            "See the warnings above for why, then restore and build the solution (dotnet build). To check "
            + "against the partial model anyway, pass --allow-workspace-diagnostics.",
            $"error: the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, "
            + "so check cannot pass: package references that resolved to nothing produce no edges, so a rule "
            + "about a package is measured against a model that never saw it:",
            "Restore the solution (dotnet restore) — any NuGet errors behind a restore that ran are in the "
            + "warnings above. To check against the partial model anyway, pass --allow-workspace-diagnostics.");
    }

    /// <summary>
    ///     The stderr lines <c>baseline</c> emits before exit 2, having written nothing. A baseline is the
    ///     team's signature on its debt: captured from a partial model it grandfathers "zero debt" for rules
    ///     that were never measured, and <c>--accept-reductions</c> deletes real entries as "no longer
    ///     occurring" when the only thing that changed is that a project stopped loading — or that its
    ///     packages stopped resolving.
    /// </summary>
    internal static string BaselineMessage(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            false,
            $"error: the model is incomplete — {Failed(diagnostics)} failed to load, so no baseline was "
            + "written: a baseline captured from a partial model signs off debt that was never measured:",
            "See the warnings above for why, then restore and build the solution (dotnet build). To baseline "
            + "against the partial model anyway, pass --allow-workspace-diagnostics.",
            $"error: the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, "
            + "so no baseline was written: every violation resting on a package edge is absent from this "
            + "model, so the baseline would sign off debt it could not see:",
            "Restore the solution (dotnet restore) — any NuGet errors behind a restore that ran are in the "
            + "warnings above. To baseline against the partial model anyway, pass "
            + "--allow-workspace-diagnostics.");
    }

    /// <summary>
    ///     The stderr lines <c>status</c> emits before exit 2, after rendering the burndown it does have.
    ///     Unloaded projects declare no types, and unresolved packages declare no edges, so every count in
    ///     that burndown reads low either way.
    /// </summary>
    internal static string StatusMessage(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            false,
            $"error: the model is incomplete — {Failed(diagnostics)} failed to load, so status cannot report "
            + "the burndown: unloaded projects contribute no violations, so every count reads low:",
            "See the warnings above for why, then restore and build the solution (dotnet build). To report "
            + "against the partial model anyway, pass --allow-workspace-diagnostics.",
            $"error: the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, "
            + "so status cannot report the burndown: a rule about a package the model never saw contributes "
            + "no violations, so every count reads low:",
            "Restore the solution (dotnet restore) — any NuGet errors behind a restore that ran are in the "
            + "warnings above. To report against the partial model anyway, pass "
            + "--allow-workspace-diagnostics.");
    }

    /// <summary>
    ///     The stderr lines <c>render</c> emits before exit 2, having written nothing. Rendered files are
    ///     committed context: a card whose project failed to load resolves no directory and is silently
    ///     dropped rather than written wrong, and <c>--diagram</c> draws the very survey <c>graph</c>
    ///     refuses to print from a partial model.
    /// </summary>
    internal static string RenderMessage(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            false,
            $"error: the model is incomplete — {Failed(diagnostics)} failed to load, so nothing was rendered: "
            + "a card whose project failed to load cannot be placed and would be dropped from the committed "
            + "files, and --diagram would draw a survey missing whole projects:",
            "See the warnings above for why, then restore and build the solution (dotnet build). To render "
            + "from the partial model anyway, pass --allow-workspace-diagnostics.",
            $"error: the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, "
            + "so nothing was rendered: a card would be committed describing dependencies the model never "
            + "resolved, and --diagram would draw a survey with the external edges missing:",
            "Restore the solution (dotnet restore) — any NuGet errors behind a restore that ran are in the "
            + "warnings above. To render from the partial model anyway, pass --allow-workspace-diagnostics.");
    }

    /// <summary>
    ///     The <c>graph</c> refusal, thrown as a <see cref="UserErrorException" /> before extraction
    ///     rather than written to a stream — so the identical text reaches CLI stderr (exit 2, via
    ///     <c>CliErrorMapper</c>) and an MCP <c>arch_graph</c> error result (via
    ///     <c>GlobalCallToolFilter</c>).
    /// </summary>
    /// <remarks>
    ///     It carries the failed projects inline rather than pointing at warnings printed above, because on
    ///     the MCP surface there is nothing above: <c>arch_graph</c> discards its error writer, so a refusal
    ///     that said "see the warnings" would name evidence the caller cannot reach. <c>graph</c> is also the
    ///     entry point that needs no spec — a stranger's first command on an unfamiliar codebase — so the
    ///     refusal names the fix in both dialects rather than assuming which surface asked.
    /// </remarks>
    internal static string GraphRefusal(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            true,
            $"the model is incomplete — {Failed(diagnostics)} failed to load, so graph cannot survey the "
            + "codebase:",
            "Restore and build the solution first (dotnet build), then retry. To survey the partial model as it "
            + "loaded, pass --allow-workspace-diagnostics (CLI) or allowWorkspaceDiagnostics: true (arch_graph).",
            $"the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, so graph "
            + "cannot survey the codebase: the external references a survey exists to show are exactly what "
            + "did not resolve:",
            "Restore the solution first (dotnet restore), then retry. To survey the partial model as it "
            + "loaded, pass --allow-workspace-diagnostics (CLI) or allowWorkspaceDiagnostics: true (arch_graph).");
    }

    /// <summary>
    ///     The refusal spec resolution throws when the convention finds no spec project and the model is
    ///     incomplete — the project that would have matched may be one the run could not see, and no
    ///     <c>--spec</c> argument repairs either cause.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Like <see cref="ContextCaveat" /> it gates nothing: it is the same two causes with the same two
    ///         remedies, said where spec resolution meets them. It belongs here rather than at the call site
    ///         because spec resolution runs <em>before</em> any verb's gate can fire, so this is the message a
    ///         reader on a broken tree actually meets — and a second, hand-rolled answer to one condition is
    ///         exactly how the four verbs came to disagree in the first place. Hand-rolled, it quoted the load
    ///         failures for either cause, so a run incomplete only because a restore failed promised a list of
    ///         failed projects and printed none.
    ///     </para>
    ///     <para>
    ///         It carries the blamed projects inline rather than pointing at warnings printed above, for
    ///         <see cref="GraphRefusal" />'s reason and a stronger one of its own: this refusal is thrown
    ///         before a runner exists to render any diagnostics beside it, so on <em>both</em> its surfaces
    ///         there is nothing above to point at.
    ///     </para>
    /// </remarks>
    internal static string SpecRefusal(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            false,
            $"No spec project found: {Failed(diagnostics)} failed to load, so a project that references "
            + "Zphil.LoadBearing.dll may be among them:",
            "Restore and build the solution first (dotnet restore, dotnet build), then retry.",
            $"No spec project found: NuGet packages did not resolve for {Unrestored(diagnostics)}, so a "
            + "project that references Zphil.LoadBearing.dll may have failed to resolve it:",
            "Restore the solution first (dotnet restore), then retry.");
    }

    /// <summary>
    ///     The caveat block <c>context</c> writes above its answer when the model is incomplete. Context
    ///     never gates — it is a lookup an agent runs mid-edit, and a partial answer beats none — but a
    ///     partial load is exactly the state in which "no architecture scope covers this path" can be a
    ///     false all-clear: a card whose project failed to load resolves no directory and places nowhere, and
    ///     a rule about a package the restore never fetched answered without ever measuring anything.
    ///     Stdout is context's only channel (no CLI twin, no <c>--json</c>), so the caveat rides the body,
    ///     the blamed projects inline, ahead of whatever answer the partial model still supports.
    /// </summary>
    internal static string ContextCaveat(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            true,
            $"caveat: the model is incomplete — {Failed(diagnostics)} failed to load, so scope cards from "
            + "them cannot be placed and \"no architecture scope covers\" cannot be trusted for paths under "
            + "them:",
            "Restore and build the solution first (dotnet build), then retry for a whole answer.",
            $"caveat: the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, "
            + "so a rule about a package cannot be trusted to have been measured for paths under them:",
            "Restore the solution first (dotnet restore), then retry for a whole answer.");
    }

    /// <summary>
    ///     The <c>Workspace_LoadedCompletely</c> failure body — the CLI gate transposed to a test report:
    ///     <c>check</c> gates and emits no verdict, so the adapter emits no rule verdicts. It carries the
    ///     blamed projects inline for <see cref="GraphRefusal" />'s exact reason: in a test report there is
    ///     nothing above to point at.
    /// </summary>
    internal static string AdapterRefusal(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            true,
            $"the model is incomplete — {Failed(diagnostics)} failed to load, so every rule test was skipped:",
            "Every rule would be measured over a codebase missing whole projects — a green run against a "
            + "partial model would sign verdicts that were never reached. Restore and build the solution "
            + "first (dotnet build), then retry. To check the partial model as it loaded, override "
            + "AllowWorkspaceDiagnostics to true on the test class.",
            $"the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, so every "
            + "rule test was skipped:",
            "Every rule about a package would be measured over a codebase where that package resolved to "
            + "nothing — a green run against a partial model would sign verdicts that were never reached. "
            + "Restore the solution first (dotnet restore), then retry. To check the partial model as it "
            + "loaded, override AllowWorkspaceDiagnostics to true on the test class.");
    }

    /// <summary>
    ///     The <c>Workspace_LoadedCompletely</c> skip reason under <c>AllowWorkspaceDiagnostics</c>: the rule
    ///     verdicts are live again, but a test by that name cannot pass while the load failures it is named
    ///     for are real — so it skips, and the failed projects ride the skip reason.
    /// </summary>
    internal static string AdapterOptedIn(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            false,
            "AllowWorkspaceDiagnostics is true: rule verdicts come from the partial model that loaded, despite "
            + $"{Failed(diagnostics)} failing to load:",
            null,
            "AllowWorkspaceDiagnostics is true: rule verdicts come from the partial model that loaded, despite "
            + $"NuGet packages not resolving for {Unrestored(diagnostics)}:",
            null);
    }

    // The count, as a noun phrase every lede can take: "1 project" / "3 projects". A gate that knows exactly
    // which projects failed has no business saying "one or more".
    private static string Failed(WorkspaceDiagnostics diagnostics)
    {
        return Counted(diagnostics.FailedProjects.Count);
    }

    private static string Unrestored(WorkspaceDiagnostics diagnostics)
    {
        return Counted(diagnostics.RestoreFailedProjects.Count);
    }

    private static string Counted(int count)
    {
        return $"{count} {Plurals.Noun(count, "project")}";
    }

    // A surface's whole refusal: one EvidenceBlock per cause it has, load failures first. Both ledes are
    // built by the caller and the unused one is simply dropped, which is what keeps a single-cause message
    // byte-identical to the one this class emitted before restore failures could gate — no branch in the
    // caller, no conditional in any literal.
    private static string Message(
        WorkspaceDiagnostics diagnostics,
        bool withSelectionNote,
        string loadLede,
        string? loadTail,
        string restoreLede,
        string? restoreTail)
    {
        var blocks = new List<string>();

        if (diagnostics.FailedProjects.Count > 0)
            blocks.Add(EvidenceBlock.Compose(loadLede, diagnostics.FailedProjects, loadTail, withSelectionNote));

        if (diagnostics.RestoreFailedProjects.Count > 0)
            blocks.Add(
                EvidenceBlock.Compose(
                    restoreLede, diagnostics.RestoreFailedProjects, restoreTail, withSelectionNote));

        return string.Join("\n", blocks);
    }
}
