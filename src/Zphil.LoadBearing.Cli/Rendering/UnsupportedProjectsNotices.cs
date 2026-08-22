using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The one place the CLI composes an unsupported-projects stamp: which declared projects the run did not
///     reach, and why.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="NarrowingNotices" />' twin, split from it because the two answer different questions
///         and take different evidence — a narrowing names the filter that caused it, while nothing caused
///         this. The wording stays in <see cref="UnsupportedProjectsNotice" /> beside its two neighbours;
///         what lives here is the CLI's half.
///     </para>
///     <para>
///         <b>
///             The entries come off <see cref="WorkspaceTrustStamp" /> rather than straight off the
///             diagnostics.
///         </b>
///         That is what makes this line and the verb's <c>--json</c> document agree by
///         construction on both halves a reader compares — how a path is spelled, and what the reason says.
///         Composing the reason here instead would be a second author for one sentence.
///     </para>
/// </remarks>
internal static class UnsupportedProjectsNotices
{
    /// <summary>
    ///     Writes the stamp above a verb's answer, or nothing at all when the run reached every declared
    ///     project.
    /// </summary>
    /// <param name="output">The verb's stdout writer — the stamp scopes what follows it there.</param>
    /// <param name="source">The run's codebase source, carrying the load's verdict and the solution root.</param>
    /// <param name="factory">The verb's stamp wording, from <see cref="UnsupportedProjectsNotice" />.</param>
    internal static void Stamp(
        TextWriter output, CodebaseSource source, Func<IReadOnlyList<string>, string> factory)
    {
        if (source.Diagnostics.UnsupportedProjects.Count == 0) return;

        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(source.Diagnostics, source.SolutionDirectory);
        IReadOnlyList<string> entries = (trust.UnsupportedProjects ?? [])
            .Select(project => project.Describe())
            .ToList();

        LineBlocks.WriteStamp(output, factory(entries));
    }
}
