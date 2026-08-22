using Zphil.LoadBearing.Codebase;
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
///     Reads <see cref="MultiTargetedProject" />s off a merged model — the one owner of the projection, so
///     every composer that holds a model fills the slot the same way.
/// </summary>
/// <remarks>
///     It lives beside the record rather than at any one caller because the record's own documentation
///     promises the slot and <c>MergeNotes</c> are filled from the same merge on the same read; a composer
///     that projected for itself could honour that promise differently.
/// </remarks>
internal static class MultiTargetedProjects
{
    /// <summary>
    ///     The projects <paramref name="codebase" /> holds that arrived as several compilations, each with
    ///     its frameworks and the one its shared types' facts came from. Empty for a solution whose projects
    ///     each target one framework, which is most of them.
    /// </summary>
    /// <remarks>
    ///     Wider than the merge notes by design: a note is raised only where two frameworks declared the same
    ///     type, so a project whose frameworks share nothing has no note and is still checked against one of
    ///     them.
    /// </remarks>
    internal static IReadOnlyList<MultiTargetedProject> Of(CodebaseModel codebase)
    {
        return codebase.Projects
            .Where(project => project.TargetFrameworks.Count > 0)
            .Select(project => new MultiTargetedProject(
                project.Name, project.TargetFrameworks, project.FactsFollow))
            .ToList();
    }
}

/// <summary>
///     Why a project the solution declares is outside the model — the classification its producer already
///     made, carried to the surfaces rather than re-derived at the edge that composes the sentence.
/// </summary>
/// <remarks>
///     Two members rather than one per language: the path already carries the extension, so per-language
///     wording would add near-identical strings without adding a fact. What earns a member of its own is a
///     project whose absence means something <em>different</em>, and only the shared project does.
/// </remarks>
internal enum UnsupportedProjectKind
{
    /// <summary>
    ///     A project in a language this product has no extractor for — an <c>.fsproj</c>, a <c>.vbproj</c>,
    ///     a <c>.sqlproj</c>, a <c>.vcxproj</c>, or a compiler invocation the replay declined as non-C#.
    /// </summary>
    NotCsharp,

    /// <summary>
    ///     A shared project (<c>.shproj</c>): a language-neutral container whose <c>.projitems</c> files are
    ///     compiled into every project that imports it. Its code is very often C#, and where an importing
    ///     project is in the model that code is in the model too — so the only honest thing to say about it
    ///     is its shape, which is what sets it apart from <see cref="NotCsharp" />.
    /// </summary>
    SharedProject
}

/// <summary>
///     One project the solution declares that no extractor reached, and the kind of reason it did not.
/// </summary>
/// <param name="Path">The absolute path to the project file.</param>
/// <param name="Kind">What kind of project it is — which is what the reason a reader sees is composed from.</param>
internal sealed record UnsupportedProject(string Path, UnsupportedProjectKind Kind);

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
///         What gates is a set of projects, not a set of sentences: the decision rests on two structural
///         facts read at the load boundary — <see cref="ProjectLoadFailures" /> off the loaded solution,
///         <see cref="RestoreFailures" /> off the assets files — and never on diagnostic text, which is
///         neither a contract nor language-independent. The diagnostics render; they decide nothing. The two
///         causes stay two lists (separate facts, separate remedies) and meet at <see cref="IsIncomplete" />,
///         because the consequence is one: a rule measured against a model missing what it is about does not
///         answer, it guesses.
///     </para>
///     <para>
///         One type rather than a handful of lists because the streams must not be swapped: the gate keys
///         strictly on the two blamed-project lists, while the rendered stream carries the rest. Held as bare
///         <c>IReadOnlyList&lt;string&gt;</c>s the swap type-checks; held here, <see cref="IsIncomplete" />
///         and <see cref="Gates" /> take no list at all and the swap is untypeable — a caller chooses only
///         between <see cref="Rendered" /> and <see cref="RenderedWithMergeNotes" />, which are both
///         rendering choices.
///     </para>
///     <para>
///         The MSBuild-selection note rides the composed list, not the write: both renderings append it
///         once, and callers hand that one list to <em>both</em> the stderr echo and the JSON document —
///         appending at write time reached stderr only, which MCP (passing <see cref="TextWriter.Null" /> as
///         the error writer) never sees. An empty input composes to an empty list, so a clean run says
///         nothing about MSBuild on any surface.
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
///     The projects the solution declares that no extractor reached — each an absolute path with the
///     <see cref="UnsupportedProjectKind" /> its producer classified it as — ordinal-sorted by path, and
///     read off the solution file rather than off the load. It takes <see cref="UncheckedProjects" />'
///     posture rather than <see cref="FailedProjects" />': a project no extractor can read makes the
///     universe smaller, never wrong, so it says what the run covers and decides nothing. The kind travels
///     from the producer that knew it rather than being re-derived at the composing edge, where an
///     extension test misreads a shared project (see <see cref="UnsupportedProjectKind.SharedProject" />).
/// </param>
/// <param name="MultiTargetedProjects">
///     The projects one <c>.csproj</c> of which yielded several compilations, each with its frameworks and
///     the one its shared types' facts came from. It is <see cref="MergeNotes" />' machine-readable half —
///     both are filled from the same merge, on the same read, so the key and the prose cannot disagree about
///     what compiled twice — and it takes <see cref="UnsupportedProjects" />' posture rather than
///     <see cref="FailedProjects" />': the model is complete and every rule answered, over one framework's
///     view of the projects named here. So it scopes the verdict, never invalidates it, and never reaches
///     <see cref="Gates" />.
/// </param>
internal readonly record struct WorkspaceDiagnostics(
    IReadOnlyList<string> LoadFailures,
    IReadOnlyList<string> MergeNotes,
    IReadOnlyList<string> FailedProjects,
    IReadOnlyList<string> UncheckedProjects,
    IReadOnlyList<string> RestoreFailedProjects,
    IReadOnlyList<UnsupportedProject> UnsupportedProjects,
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
