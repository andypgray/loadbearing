using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Roslyn.Diagnostics;

/// <summary>
///     One project file that arrived as several compilations: its name, every target framework it was
///     extracted from, and the one whose facts the types those frameworks share ended up carrying.
/// </summary>
/// <remarks>
///     <para>
///         It lands here rather than beside the document DTOs because it is a fact about the extraction, and
///         because a document that composed it for itself could disagree with the merge note stating the same
///         thing in prose. Unlike the four project lists around it these are project <em>names</em>, never
///         paths, so nothing relativizes them and the value reaches the wire as it is.
///     </para>
///     <para>
///         <see cref="FactsFollow" /> is null when the frameworks share no type. That is the gate the merge's
///         own note is under, and it is a correctness matter rather than a nicety: a project whose frameworks
///         each declare their own types displaced nothing, and naming a winner would be false about all of
///         them. The framework list alone still says the project compiled more than once.
///     </para>
/// </remarks>
/// <param name="Project">The project (assembly) name — one name for however many compilations it produced.</param>
/// <param name="TargetFrameworks">
///     Every framework the project was extracted from, ordinal-ordered — which is also the order a workspace
///     load hands them over in, so the first entry is the one a shared type's facts fall to.
/// </param>
/// <param name="FactsFollow">
///     The framework whose facts the shared types carry, or <see langword="null" /> when the frameworks share
///     no type.
/// </param>
internal sealed record MultiTargetedProject(
    string Project,
    IReadOnlyList<string> TargetFrameworks,
    string? FactsFollow);

/// <summary>
///     Everything a run knows about how well its workspace loaded, as one value: the
///     <see cref="FailedProjects" /> and <see cref="RestoreFailedProjects" /> that gate, the
///     <see cref="LoadFailures" /> and <see cref="MergeNotes" /> that never do, the
///     <see cref="UncheckedProjects" />, <see cref="UnsupportedProjects" /> and
///     <see cref="MultiTargetedProjects" /> that scope the verdict instead of deciding it, plus the rendering
///     both surfaces read and the gate decision every verb makes.
/// </summary>
/// <remarks>
///     <para>
///         <b>What gates is a set of projects, not a set of sentences.</b> Every MSBuild project-load log
///         item — warnings included — reaches a host as
///         <see cref="Microsoft.CodeAnalysis.WorkspaceDiagnosticKind.Failure" /> with no code, so deciding
///         "did the model fail to build" from that stream means matching message text, and message text is
///         neither a contract nor language-independent. It refused solutions whose rules all passed (a NuGet
///         pruning advisory) and refused in German what it let through in English (the same audit-fetch
///         failure). So the decision moved off the text entirely, onto two structural facts read at the load
///         boundary: <see cref="ProjectLoadFailures" /> reads which projects failed off the loaded solution's
///         own structure, and <see cref="RestoreFailures" /> reads which projects' packages are not in the
///         model off the assets files they point at. This type carries both answers to every surface. The
///         diagnostics render; they decide nothing.
///     </para>
///     <para>
///         <b>Two causes, one gate.</b> They are separate lists because they are separate facts with separate
///         remedies — a project that never loaded declares no types at all, while one whose packages did not
///         resolve loaded completely and is merely missing every package edge — but they meet at
///         <see cref="IsIncomplete" />, because the consequence is the same in the only way that decides
///         anything: a rule measured against a model missing what it is about does not answer, it guesses.
///         Measured on a purpose-built bed with the feed as the only variable: exit 1 restored, exit 0 broken,
///         and the missing external edge visible in <c>graph --json</c>.
///     </para>
///     <para>
///         <b>Why one type rather than a handful of lists.</b> The streams must not be swapped: the gate keys
///         strictly on the two blamed-project lists, while the rendered stream carries the load diagnostics, the
///         MSBuild-selection note and — for <c>check</c> — the merge notes as well. Held as bare
///         <c>IReadOnlyList&lt;string&gt;</c>s the swap type-checks, so every consumer had to carry a comment
///         warning against it. Held here, <see cref="IsIncomplete" /> and <see cref="Gates" /> take no list
///         at all and the swap is untypeable; a caller chooses only between <see cref="Rendered" /> and
///         <see cref="RenderedWithMergeNotes" />, which are both rendering choices.
///     </para>
///     <para>
///         <b>The MSBuild-selection note rides the composed list, not the write.</b> Both renderings append
///         it once, and callers hand that one list to <em>both</em> the stderr echo and the JSON document.
///         That is what makes it reachable from MCP, where the tools pass <see cref="TextWriter.Null" /> as
///         the error writer: appending at write time reached stderr only.
///     </para>
///     <para>
///         <b>Quiet runs stay quiet.</b> An empty input composes to an empty list, so a clean run says
///         nothing about MSBuild on any surface. The note is diagnostic context, not a banner.
///     </para>
/// </remarks>
/// <param name="LoadFailures">
///     The workspace-load failure diagnostics. Rendered on every surface, and quoted by a refusal that has
///     no other channel — but never a gate input: MSBuild severity does not survive the trip, so an entry
///     here can be a fatal evaluation error or a restore warning and nothing downstream can tell.
/// </param>
/// <param name="MergeNotes">
///     The advisory notes the fragment merge raised (same-FQN cross-project conflation, a multi-framework
///     collapse, a name a referenced assembly also supplies). Informational:
///     they ride the rendered stream where a caller asks for them, and never gate.
/// </param>
/// <param name="FailedProjects">
///     The absolute <c>.csproj</c> paths of the projects that failed to load, ordinal-sorted — half the
///     fail-closed gate's input, and the evidence a refusal names.
/// </param>
/// <param name="UncheckedProjects">
///     The absolute <c>.csproj</c> paths the solution declares that this run did not check, ordinal-sorted —
///     non-empty only under a solution filter that left members out. It scopes the verdict rather than
///     invalidating it, so it never reaches <see cref="Gates" />.
/// </param>
/// <param name="RestoreFailedProjects">
///     The absolute <c>.csproj</c> paths of the projects whose NuGet packages are not in the model —
///     restore ran and failed, or never ran — ordinal-sorted. The gate's other input, and disjoint from
///     <see cref="FailedProjects" /> by construction. These projects loaded completely; what they are missing
///     is every edge their package references would have produced, and the two causes share one slot because
///     they share one remedy (<c>dotnet restore</c>) and one consequence.
/// </param>
/// <param name="UnsupportedProjects">
///     The absolute paths of the projects the solution declares in a language this product cannot read —
///     an <c>.fsproj</c>, a <c>.vbproj</c>, a <c>.sqlproj</c> — ordinal-sorted, and read off the solution
///     file rather than off the load. It takes <see cref="UncheckedProjects" />' posture rather than
///     <see cref="FailedProjects" />': a project no extractor can read makes the universe smaller, never
///     wrong, so it says what the run covers and decides nothing. Without it the run simply surveyed fewer
///     projects than the solution declares and said so nowhere.
/// </param>
/// <param name="MultiTargetedProjects">
///     The projects one <c>.csproj</c> of which yielded several compilations, each with its frameworks and
///     the one its shared types' facts came from. It is <see cref="MergeNotes" />' machine-readable half —
///     both are filled from the same merge, on the same read, so the key and the prose cannot disagree about
///     what compiled twice — and it takes <see cref="UnsupportedProjects" />' posture rather than
///     <see cref="FailedProjects" />': the model is complete and every rule answered, over one framework's
///     view of the projects named here. So it scopes the verdict, never invalidates it, and never reaches
///     <see cref="Gates" />. Wider than the notes, deliberately: a note is raised only where two frameworks
///     declared the same type, while a project whose frameworks share nothing is still a project that
///     compiled more than once, and that is unsayable in prose the merge does not raise.
/// </param>
internal readonly record struct WorkspaceDiagnostics(
    IReadOnlyList<string> LoadFailures,
    IReadOnlyList<string> MergeNotes,
    IReadOnlyList<string> FailedProjects,
    IReadOnlyList<string> UncheckedProjects,
    IReadOnlyList<string> RestoreFailedProjects,
    IReadOnlyList<string> UnsupportedProjects,
    IReadOnlyList<MultiTargetedProject> MultiTargetedProjects)
{
    /// <summary>A run with nothing to report — nothing failed to load, and no diagnostics or merge notes.</summary>
    internal static WorkspaceDiagnostics None { get; } = new([], [], [], [], [], [], []);

    /// <summary>
    ///     The load failures with the MSBuild-selection note appended, or empty for a clean load. What five
    ///     of the six rendering verbs echo to stderr and stamp into their document.
    /// </summary>
    internal IReadOnlyList<string> Rendered => Compose(LoadFailures);

    /// <summary>
    ///     The load failures <em>and</em> the merge notes with the MSBuild-selection note appended, or empty
    ///     when there is neither. <c>check</c> alone renders this: it is the one verb that has already
    ///     extracted by the time it renders, so it is the only one whose merge notes exist yet, and the
    ///     merge advisories are read there beside the violations they can explain.
    /// </summary>
    internal IReadOnlyList<string> RenderedWithMergeNotes => Compose([.. LoadFailures, .. MergeNotes]);

    /// <summary>
    ///     The load diagnostics that say something about <em>this</em> solution: every one that is not a
    ///     NuGetAudit advisory.
    /// </summary>
    /// <remarks>
    ///     An advisory's publication date and an audit fetch's network reachability are external, time-varying
    ///     inputs — they say nothing about how this codebase is built — so a message that offers the load as
    ///     a possible explanation for something is better off not counting them. This is a
    ///     <em>presentation</em> distinction and nothing more: it orders and bounds what a refusal quotes,
    ///     and it decides no verdict. What gates is <see cref="FailedProjects" /> and
    ///     <see cref="RestoreFailedProjects" />, neither of which any message text can influence in any
    ///     language.
    /// </remarks>
    internal IReadOnlyList<string> ActionableDiagnostics =>
        LoadFailures.Where(diagnostic => !NuGetAuditDiagnostics.IsAudit(diagnostic))
            .ToList();

    /// <summary>
    ///     Whether the model is incomplete — at least one project failed to load, or at least one project's
    ///     NuGet packages did not resolve — independently of whether the operator opted in. This is the fact
    ///     the JSON documents carry: the opt-out changes the exit code, never the truth about the model.
    /// </summary>
    internal bool IsIncomplete => FailedProjects.Count > 0 || RestoreFailedProjects.Count > 0;

    /// <summary>
    ///     Whether a solution filter narrowed the universe — at least one declared project went unchecked.
    ///     Never a gate input, unlike <see cref="IsIncomplete" />: a narrowed run is a smaller true answer, so
    ///     this scopes the verdict rather than deciding it, and the verbs that read absence as evidence
    ///     (<c>baseline --init</c>, <c>--accept-reductions</c>, <c>render</c>) refuse on their own terms.
    /// </summary>
    internal bool IsNarrowed => UncheckedProjects.Count > 0;

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
