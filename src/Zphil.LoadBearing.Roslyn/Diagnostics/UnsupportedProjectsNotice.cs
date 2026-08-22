using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn.Diagnostics;

/// <summary>
///     The wording each verb uses to say that the solution declares projects the run did not survey — the
///     human twin of the documents' <c>unsupportedProjects</c> slot.
/// </summary>
/// <remarks>
///     <para>
///         <b>The third subject to reach <see cref="EvidenceBlock" />, and the quietest.</b> Nothing here
///         is anybody's mistake and there is no remedy to offer — no argument to this run would have
///         widened it — so every tail closes on the reading being denied rather than on a command.
///     </para>
///     <para>
///         <b>Per-verb tails rather than one parameterized string</b>, on the two neighbours' reasoning:
///         they share a shape, not a template — each says what <em>that</em> verb's answer is missing.
///     </para>
///     <para>
///         <b>No <c>graph</c> tail, deliberately.</b> The survey states this as a section of its own roster
///         rather than as a stamp above it, because the survey <em>is</em> a list of projects and the missing
///         ones belong beside the ones that are there. A stamp as well would say it twice.
///     </para>
///     <para>
///         <b>The per-entry sentence lives here too, not at the surfaces.</b> <see cref="Reason" /> turns a
///         producer's <see cref="UnsupportedProjectKind" /> into words and <see cref="Entry" /> joins it to
///         the project; the CLI line, the survey's own section and the adapter's skip all read them, so one
///         entry cannot be spelled three ways. It sits beside the ledes because the two have to agree: a
///         lede that named a cause would be restating — or contradicting — what every entry under it says.
///     </para>
/// </remarks>
internal static class UnsupportedProjectsNotice
{
    /// <summary>
    ///     Why a project of this <paramref name="kind" /> is outside the model, as a reader reads it — the
    ///     one author of that sentence, on every surface.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         No line carries an em dash: <see cref="Entry" /> already joins on one, and a reason with a
    ///         second would leave the line with two.
    ///     </para>
    ///     <para>
    ///         <b>
    ///             The fallback arm is <see cref="UnsupportedProjectKind.NotCsharp" />, and a test is what
    ///             guards it.
    ///         </b>
    ///         That kind is the catch-all classification, so falling to it is the right
    ///         default — but it also means a kind added without wording is quietly described as the wrong
    ///         thing, which is the defect this taxonomy exists to retire. The compiler cannot help: dropping
    ///         the arm to make a missing member a warning makes every <em>unnamed</em> value one instead
    ///         (CS8524), permanently. So <c>UnsupportedProjectsNoticeTests</c> asserts that every declared
    ///         kind reads differently from the others, which reds on exactly the omission this arm allows.
    ///     </para>
    /// </remarks>
    /// <param name="kind">The classification the project's producer made.</param>
    internal static string Reason(UnsupportedProjectKind kind)
    {
        return kind switch
        {
            UnsupportedProjectKind.SharedProject => "a shared project, compiled into the projects that import it",
            _ => "not a C# project"
        };
    }

    /// <summary>
    ///     The entry as a human line reads it: the project, then why. One owner, so the survey's own section
    ///     and the stamp a verb writes above its answer cannot spell the same entry two ways.
    /// </summary>
    /// <param name="project">The project, as the surface spells it (solution-relative, forward-slashed).</param>
    /// <param name="reason">Why the run did not reach it, from <see cref="Reason" />.</param>
    internal static string Entry(string project, string reason)
    {
        return $"{project} — {reason}";
    }

    /// <summary>
    ///     The unsupported projects as a notice shows them: one composed <see cref="Entry" /> per project,
    ///     each path solution-relative and forward-slashed like every path beside it, so a machine path never
    ///     lands in output a golden pins.
    /// </summary>
    /// <remarks>
    ///     <see cref="NarrowedUniverseNotice.Relative" />'s twin, and it composes where that one only
    ///     relativizes: the reason is half of what an entry says, and a caller that had to add it would be a
    ///     second author for this file's sentence. The CLI does not come through here — it relativizes
    ///     through the shared trust stamp, which is where a path becomes the thing a document prints — so
    ///     this serves the adapter, which has no document.
    /// </remarks>
    /// <param name="projects">The projects, with their kinds.</param>
    /// <param name="solutionDirectory">The directory their paths are shown relative to.</param>
    internal static IReadOnlyList<string> Relative(
        IReadOnlyList<UnsupportedProject> projects, string solutionDirectory)
    {
        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        return projects
            .Select(project => Entry(relativizer.Relative(project.Path), Reason(project.Kind)))
            .ToList();
    }

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

    /// <summary>
    ///     The <c>Workspace_LoadedCompletely</c> skip reason over a solution declaring projects this run
    ///     could not reach — the adapter's twin of the CLI stamps, and a skip rather than a pass for
    ///     <see cref="NarrowedUniverseNotice.AdapterSkip" />'s reason: the rule verdicts are real, but a test
    ///     by that name cannot pass while the solution declares projects the model never held.
    /// </summary>
    /// <param name="unsupportedProjects">The solution-relative entries, each already carrying its reason.</param>
    internal static string AdapterSkip(IReadOnlyList<string> unsupportedProjects)
    {
        return Block(
            unsupportedProjects,
            "Rule verdicts come from the projects this product can read, but a test by this name cannot pass "
            + "while the solution declares projects the model never held.");
    }

    // The stamps' own lede — fixed, because a stamp scopes an answer that still follows and has nothing else
    // to say first. Deliberately no remedy: unlike its two neighbours there is no command that would widen
    // the answer, and inventing one would be worse than saying nothing.
    private static string Block(IReadOnlyList<string> unsupportedProjects, string tail)
    {
        var lede = $"{Subject(unsupportedProjects.Count)}:";
        return EvidenceBlock.Compose(lede, unsupportedProjects, tail);
    }

    // The count as a clause every lede can take, in both numbers — the sentence a reader stops trusting is
    // "1 projects ... were not surveyed". Noun and copula both come from the one inflection owner, which is
    // where NarrowedUniverseNotice.Subject takes the same two words from: the clause is this file's, the
    // English is nobody's.
    //
    // It states no cause: a cause clause would duplicate what every entry beneath it says — and "C# only"
    // would be false about a shared project, whose code is very often C#. A lede scopes; the entries say
    // why, each for itself.
    private static string Subject(int unsupportedProjectCount)
    {
        string noun = Plurals.Noun(unsupportedProjectCount, "project");
        string copula = Plurals.PastVerb(unsupportedProjectCount);

        return $"{unsupportedProjectCount} {noun} the solution declares {copula} not surveyed";
    }
}
