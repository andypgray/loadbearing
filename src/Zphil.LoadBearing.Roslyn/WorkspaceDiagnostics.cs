using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Everything a run knows about how well its workspace loaded, as one value: the
///     <see cref="LoadFailures" /> that gate and the advisory <see cref="MergeNotes" /> that never do,
///     plus the rendering both surfaces read and the gate decision every verb makes.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why one type rather than two lists.</b> The two streams must not be swapped: the gate keys
///         strictly on load failures, while the rendered stream carries the MSBuild-selection note and — for
///         <c>check</c> — the merge notes as well. Held as two bare <c>IReadOnlyList&lt;string&gt;</c>s the
///         swap type-checks, so every consumer had to carry a comment warning against it. Held here,
///         <see cref="IsIncomplete" /> and <see cref="Gates" /> take no list at all and the swap is
///         untypeable; a caller chooses only between <see cref="Rendered" /> and
///         <see cref="RenderedWithMergeNotes" />, which are both rendering choices.
///     </para>
///     <para>
///         <b>The MSBuild-selection note rides the composed list, not the write.</b> Both renderings append
///         it once, and callers hand that one list to <em>both</em> the stderr echo and the JSON document.
///         That is what makes it reachable from MCP, where the tools pass <see cref="TextWriter.Null" /> as
///         the error writer: appending at write time reached stderr only. It is <em>not</em> a workspace
///         diagnostic — it never enters <see cref="LoadFailures" />, because an informational line there
///         would flip <c>check</c>'s exit code to 2 on every run.
///     </para>
///     <para>
///         <b>Quiet runs stay quiet.</b> An empty input composes to an empty list, so a clean run says
///         nothing about MSBuild on any surface. The note is diagnostic context, not a banner.
///     </para>
/// </remarks>
/// <param name="LoadFailures">
///     The workspace-load failure diagnostics, and only those — the fail-closed gate's whole input.
/// </param>
/// <param name="MergeNotes">
///     The advisory notes the fragment merge raised (same-FQN cross-project conflation). Informational:
///     they ride the rendered stream where a caller asks for them, and never gate.
/// </param>
internal readonly record struct WorkspaceDiagnostics(
    IReadOnlyList<string> LoadFailures,
    IReadOnlyList<string> MergeNotes)
{
    /// <summary>A run with nothing to report — no load failures and no merge notes.</summary>
    internal static WorkspaceDiagnostics None { get; } = new([], []);

    /// <summary>
    ///     The load failures with the MSBuild-selection note appended, or empty for a clean load. What five
    ///     of the six rendering verbs echo to stderr and stamp into their document.
    /// </summary>
    internal IReadOnlyList<string> Rendered => Compose(LoadFailures);

    /// <summary>
    ///     The load failures <em>and</em> the merge notes with the MSBuild-selection note appended, or empty
    ///     when there is neither. <c>check</c> alone renders this: it is the one verb that has already
    ///     extracted by the time it renders, so it is the only one whose merge notes exist yet, and the
    ///     same-FQN conflation advisories are read there beside the violations they can explain.
    /// </summary>
    internal IReadOnlyList<string> RenderedWithMergeNotes => Compose([.. LoadFailures, .. MergeNotes]);

    /// <summary>
    ///     Whether the model is incomplete — a load failure that is not a NuGetAudit advisory — independently
    ///     of whether the operator opted in. This is the fact the JSON documents carry: the opt-out changes
    ///     the exit code, never the truth about the model.
    /// </summary>
    internal bool IsIncomplete => LoadFailures.Any(diagnostic => !NuGetAuditDiagnostics.IsAudit(diagnostic));

    /// <summary>
    ///     Whether the fail-closed gate fires: the model is incomplete and the caller did not opt into the
    ///     partial model. The verbs that render before gating ask twice over — once for the verdict they
    ///     stamp into their document, once for the exit code — so this stays a pure predicate.
    /// </summary>
    /// <param name="allowWorkspaceDiagnostics">Whether the operator opted into the partial model.</param>
    internal bool Gates(bool allowWorkspaceDiagnostics)
    {
        return !allowWorkspaceDiagnostics && IsIncomplete;
    }

    private static IReadOnlyList<string> Compose(IReadOnlyList<string> diagnostics)
    {
        return diagnostics.Count == 0 ? [] : [.. diagnostics, MsBuildBootstrap.SelectionNote()];
    }
}
