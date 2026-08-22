using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Roslyn.Diagnostics;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The one place the CLI composes a narrowing notice: which solution filter narrowed the run, and which
///     declared projects it left unchecked.
/// </summary>
/// <remarks>
///     <para>
///         What lives here is the CLI's half — read the unchecked projects off
///         <see cref="CodebaseSource.Diagnostics" />, name the filter by its file name, relativize the paths
///         against the solution directory. The wording stays in <see cref="NarrowedUniverseNotice" />,
///         because the xUnit adapter renders the same vocabulary and has no <see cref="CodebaseSource" />
///         to hand.
///     </para>
///     <para>
///         Two forms, because a narrowed universe means two different things. <see cref="Stamp" /> scopes an
///         answer the verb still gives, so it guards itself — a run that narrowed nothing writes nothing —
///         and trails a blank line before that answer. <see cref="Refusal" /> is the whole of what a verb
///         that reads absence as evidence has to say, so it writes no trailing blank and leaves the guard and
///         the exit code to the caller, which is where the per-verb condition lives (<c>baseline</c> refuses
///         for two of its three modes).
///     </para>
/// </remarks>
internal static class NarrowingNotices
{
    /// <summary>
    ///     Writes the narrowing stamp above a verb's answer, or nothing at all when the run was not narrowed.
    /// </summary>
    /// <param name="output">The verb's stdout writer — the stamp scopes what follows it there.</param>
    /// <param name="source">The run's codebase source, carrying both the filter and what went unchecked.</param>
    /// <param name="factory">The verb's stamp wording, from <see cref="NarrowedUniverseNotice" />.</param>
    internal static void Stamp(
        TextWriter output, CodebaseSource source, Func<string, IReadOnlyList<string>, string> factory)
    {
        if (!source.Diagnostics.IsNarrowed) return;

        string stamp = Compose(source, factory);
        LineBlocks.WriteStamp(output, stamp);
    }

    /// <summary>
    ///     Writes the narrowing refusal a verb emits instead of an answer. Unguarded: the caller decides
    ///     whether this run refuses, and returns the exit code that goes with it.
    /// </summary>
    /// <param name="error">The verb's stderr writer.</param>
    /// <param name="source">The run's codebase source, carrying both the filter and what went unchecked.</param>
    /// <param name="factory">The verb's refusal wording, from <see cref="NarrowedUniverseNotice" />.</param>
    internal static void Refusal(
        TextWriter error, CodebaseSource source, Func<string, IReadOnlyList<string>, string> factory)
    {
        string refusal = Compose(source, factory);
        LineBlocks.Write(error, refusal);
    }

    private static string Compose(CodebaseSource source, Func<string, IReadOnlyList<string>, string> factory)
    {
        IReadOnlyList<string> uncheckedProjects = NarrowedUniverseNotice.Relative(
            source.Diagnostics.UncheckedProjects, source.SolutionDirectory);

        return factory(source.SolutionName, uncheckedProjects);
    }
}
