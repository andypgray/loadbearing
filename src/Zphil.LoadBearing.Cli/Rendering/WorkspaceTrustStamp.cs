using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     One declared project the run did not reach, and why — the entry shape of
///     <see cref="WorkspaceTrustStamp.UnsupportedProjects" />.
/// </summary>
/// <remarks>
///     <b>An object rather than a bare path, and still one reason string on the wire.</b> The object is what
///     lets a consumer that needs to branch take a <em>kind</em> without minting a second key — an option
///     deliberately unspent, because nothing downstream branches yet. What the entry carries is the sentence
///     <see cref="UnsupportedProjectsNotice.Reason" /> composes from the kind the producer classified, so
///     the wording is one author's and the shape here does not have to grow to say a second thing.
/// </remarks>
/// <param name="Project">The project, solution-relative and forward-slashed like every other path.</param>
/// <param name="Reason">Why the run did not reach it.</param>
internal sealed record UnsupportedProjectStamp(string Project, string Reason)
{
    /// <summary>
    ///     The entry as a human line reads it: the project, then why. Deferred to the one owner of that join,
    ///     so the survey's own section, the stamp a verb writes above its answer and the adapter's skip
    ///     cannot spell the same entry three ways.
    /// </summary>
    internal string Describe()
    {
        return UnsupportedProjectsNotice.Entry(Project, Reason);
    }
}

/// <summary>
///     The six facts every document stamps about how far its own contents can be trusted: whether the model
///     is incomplete, and which projects failed to load, went unchecked, had no packages restored, were
///     outside anything this product can survey, or were read from one of several compilations.
/// </summary>
/// <remarks>
///     <para>
///         Three of the six are adjacent lists of the same type, which is a swap the compiler cannot see: a
///         reorder exchanging what failed to load with what a solution filter left unchecked type-checks
///         cleanly and reaches the wire. Composed once here, off one <see cref="WorkspaceDiagnostics" /> and
///         by name, that reorder is untypeable — and no document decides on its own which list is which.
///         <see cref="UnsupportedProjects" /> is a different type for a different reason, but it lands here
///         for the same one: this is where a path becomes the thing a document prints.
///     </para>
///     <para>
///         The empty-to-null policy is the other half. A project list that is empty says nothing, so it is
///         carried as <see langword="null" /> and the key is omitted rather than written as <c>[]</c> — which
///         is what keeps a clean document byte-identical to the one it always was. Every document still
///         declares its own slots and their order; SARIF reads the same stamp and unwraps the nulls, because
///         an empty list emits no notification there, which is the same omission.
///     </para>
///     <para>
///         <b>The reason text is read here, once.</b> Four surfaces state it — three JSON documents and the
///         SARIF log — and asking for it at each would let the wire key and the prose disagree about what the
///         run reached. The sentence itself belongs to <see cref="UnsupportedProjectsNotice" />, beside the
///         human stamps that have to agree with it; what this type owns is the pairing, because this is the
///         only place that knows the path is about to be relativized, which is what the reader will actually
///         see.
///     </para>
/// </remarks>
/// <param name="ModelIncomplete">
///     <see langword="true" /> when a project failed to load or a project's packages did not resolve, or
///     <see langword="null" /> when the model is whole.
/// </param>
/// <param name="FailedProjects">The projects that failed to load, or <see langword="null" /> when none did.</param>
/// <param name="UncheckedProjects">
///     The declared projects a solution filter left unchecked, or <see langword="null" /> when the run
///     covered the whole solution.
/// </param>
/// <param name="RestoreFailedProjects">
///     The projects whose NuGet packages are not in the model, or <see langword="null" /> when they all are.
/// </param>
/// <param name="UnsupportedProjects">
///     The declared projects no extractor reached, each with its reason, or <see langword="null" /> when the
///     run reached them all. Unlike its three siblings this is not a verdict about the load — nothing here
///     was ever going to load — so it never reaches <see cref="ModelIncomplete" />.
/// </param>
/// <param name="MultiTargetedProjects">
///     The projects one <c>.csproj</c> of which yielded several compilations, or <see langword="null" /> when
///     every project targets one framework — which is every project of most solutions. It takes
///     <see cref="UnsupportedProjects" />' posture rather than <see cref="FailedProjects" />': the model is
///     whole and every rule answered, over one framework's view of these projects, so it never reaches
///     <see cref="ModelIncomplete" /> either. Carried through unchanged rather than remade the way the four
///     path lists are — these are project names, so there is nothing to relativize and no second shape for
///     the wire to disagree with the merge about.
/// </param>
internal readonly record struct WorkspaceTrustStamp(
    bool? ModelIncomplete,
    IReadOnlyList<string>? FailedProjects,
    IReadOnlyList<string>? UncheckedProjects,
    IReadOnlyList<string>? RestoreFailedProjects,
    IReadOnlyList<UnsupportedProjectStamp>? UnsupportedProjects,
    IReadOnlyList<MultiTargetedProject>? MultiTargetedProjects)
{
    /// <summary>
    ///     Reads the stamp off a load's own verdict, relativizing every project path.
    /// </summary>
    /// <param name="diagnostics">The load's verdict, carrying the four project lists.</param>
    /// <param name="relativizer">
    ///     The document's relativizer — the paths land solution-relative and forward-slashed, like every
    ///     other path in it, so a machine path never reaches a golden.
    /// </param>
    internal static WorkspaceTrustStamp From(WorkspaceDiagnostics diagnostics, PathFormat.Relativizer relativizer)
    {
        return new WorkspaceTrustStamp(
            diagnostics.IsIncomplete ? true : null,
            Relative(diagnostics.FailedProjects, relativizer),
            Relative(diagnostics.UncheckedProjects, relativizer),
            Relative(diagnostics.RestoreFailedProjects, relativizer),
            Unsupported(diagnostics.UnsupportedProjects, relativizer),
            diagnostics.MultiTargetedProjects.Count == 0 ? null : diagnostics.MultiTargetedProjects);
    }

    /// <summary>
    ///     The same stamp for a caller whose only path work is this one: the relativizer every project path
    ///     lands through is built from <paramref name="solutionDirectory" /> here rather than at the call site.
    /// </summary>
    /// <param name="diagnostics">The load's verdict, carrying the four project lists.</param>
    /// <param name="solutionDirectory">The directory the paths are shown relative to.</param>
    internal static WorkspaceTrustStamp From(WorkspaceDiagnostics diagnostics, string solutionDirectory)
    {
        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        return From(diagnostics, relativizer);
    }

    private static IReadOnlyList<string>? Relative(
        IReadOnlyList<string> projects, PathFormat.Relativizer relativizer)
    {
        return projects.Count == 0
            ? null
            : projects.Select(relativizer.Relative)
                .ToList();
    }

    private static IReadOnlyList<UnsupportedProjectStamp>? Unsupported(
        IReadOnlyList<UnsupportedProject> projects, PathFormat.Relativizer relativizer)
    {
        return projects.Count == 0
            ? null
            : projects.Select(project => new UnsupportedProjectStamp(
                    relativizer.Relative(project.Path), UnsupportedProjectsNotice.Reason(project.Kind)))
                .ToList();
    }
}
