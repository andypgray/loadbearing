using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The four facts every document stamps about how far its own contents can be trusted: whether the model
///     is incomplete, and which projects failed to load, went unchecked, or had no packages restored.
/// </summary>
/// <remarks>
///     <para>
///         Three of the four are adjacent lists of the same type, which is a swap the compiler cannot see: a
///         reorder exchanging what failed to load with what a solution filter left unchecked type-checks
///         cleanly and reaches the wire. Composed once here, off one <see cref="WorkspaceDiagnostics" /> and
///         by name, that reorder is untypeable — and no document decides on its own which list is which.
///     </para>
///     <para>
///         The empty-to-null policy is the other half. A project list that is empty says nothing, so it is
///         carried as <see langword="null" /> and the key is omitted rather than written as <c>[]</c> — which
///         is what keeps a clean document byte-identical to the one it always was. Every document still
///         declares its own slots and their order; SARIF reads the same stamp and unwraps the nulls, because
///         an empty list emits no notification there, which is the same omission.
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
internal readonly record struct WorkspaceTrustStamp(
    bool? ModelIncomplete,
    IReadOnlyList<string>? FailedProjects,
    IReadOnlyList<string>? UncheckedProjects,
    IReadOnlyList<string>? RestoreFailedProjects)
{
    /// <summary>
    ///     Reads the stamp off a load's own verdict, relativizing every project path.
    /// </summary>
    /// <param name="diagnostics">The load's verdict, carrying the three project lists.</param>
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
            Relative(diagnostics.RestoreFailedProjects, relativizer));
    }

    private static IReadOnlyList<string>? Relative(
        IReadOnlyList<string> projects, PathFormat.Relativizer relativizer)
    {
        return projects.Count == 0
            ? null
            : projects.Select(relativizer.Relative)
                .ToList();
    }
}
