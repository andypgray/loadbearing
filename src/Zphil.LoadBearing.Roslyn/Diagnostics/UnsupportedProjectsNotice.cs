using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn.Diagnostics;

/// <summary>
///     The wording each verb uses to say that the solution declares projects this product cannot read — the
///     human twin of the documents' <c>unsupportedProjects</c> slot.
/// </summary>
/// <remarks>
///     <para>
///         <b>The third subject to reach <see cref="EvidenceBlock" />, and the quietest.</b>
///         <see cref="IncompleteModelGate" /> says the model is <em>wrong</em> and refuses;
///         <see cref="NarrowedUniverseNotice" /> says the operator asked for less than the solution and
///         scopes; this one says the product itself reaches less than the solution, and scopes for the same
///         reason. Nothing here is anybody's mistake and there is no remedy to offer — which is exactly why
///         it had to be said out loud rather than left to a shorter list.
///     </para>
///     <para>
///         <b>Per-verb tails rather than one parameterized string</b>, on the two neighbours' reasoning: they
///         share a shape, not a template. Each says what <em>that</em> verb's answer is missing — a verdict
///         that cannot be clean for the solution, counts that read low and always will.
///     </para>
///     <para>
///         <b>No <c>graph</c> tail, deliberately.</b> The survey states this as a section of its own roster
///         rather than as a stamp above it, because the survey <em>is</em> a list of projects and the missing
///         ones belong beside the ones that are there. A stamp as well would say it twice.
///     </para>
/// </remarks>
internal static class UnsupportedProjectsNotice
{
    /// <summary>
    ///     The stamp <c>check</c> writes above its report. A rule cannot be violated in a project the model
    ///     does not contain, so a clean verdict over a polyglot solution is the reading this prevents.
    /// </summary>
    /// <param name="unsupportedProjects">The solution-relative paths, each with its reason.</param>
    internal static string CheckStamp(IReadOnlyList<string> unsupportedProjects)
    {
        return Block(
            unsupportedProjects,
            "The verdict below covers only the projects this product can read, so a clean result here is not "
            + "a clean solution.");
    }

    /// <summary>
    ///     The stamp <c>status</c> writes above its burndown. Unlike a filter's narrowing, this zero is
    ///     permanent: no future run of this product will find debt in these projects to burn down.
    /// </summary>
    /// <param name="unsupportedProjects">The solution-relative paths, each with its reason.</param>
    internal static string StatusStamp(IReadOnlyList<string> unsupportedProjects)
    {
        return Block(
            unsupportedProjects,
            "The burndown below counts only the projects this product can read: these contribute no "
            + "violations and never will, so every count reads low by whatever they hold.");
    }

    // The stamps' own lede — fixed, because a stamp scopes an answer that still follows and has nothing else
    // to say first. Deliberately no remedy: unlike its two neighbours there is no command that would widen
    // the answer, and inventing one would be worse than saying nothing.
    private static string Block(IReadOnlyList<string> unsupportedProjects, string tail)
    {
        var lede = $"{Subject(unsupportedProjects.Count)}:";
        return EvidenceBlock.Compose(lede, unsupportedProjects, tail);
    }

    // The count as a clause both ledes can take, in both numbers — the sentence a reader stops trusting is
    // "1 projects ... could not be read". Noun and copula both come from the one inflection owner, which is
    // where NarrowedUniverseNotice.Subject takes the same two words from: the clause is this file's, the
    // English is nobody's.
    private static string Subject(int unsupportedProjectCount)
    {
        string noun = Plurals.Noun(unsupportedProjectCount, "project");
        string copula = Plurals.PastVerb(unsupportedProjectCount);

        return $"{unsupportedProjectCount} {noun} the solution declares {copula} not read — this product "
               + "surveys C# projects only";
    }
}
