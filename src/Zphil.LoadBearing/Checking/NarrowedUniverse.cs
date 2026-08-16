using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     The fact that a solution filter answered this run over part of the solution — what turns a rule's
///     empty subject from a spec defect into an absence the run cannot speak to (GRAMMAR §4.1).
/// </summary>
/// <remarks>
///     <para>
///         Null on every unfiltered run, and on a filtered run that narrowed nothing, so a run whose
///         universe is the whole solution reaches exactly the verdicts it always did.
///     </para>
///     <para>
///         <b>The reason arrives composed rather than assembled here.</b> The narrowing lexicon lives with
///         the stamps every verb writes, so a wording minted in Core would drift from them silently; and
///         Core cannot relativize or name a project path anyway (no <c>Path.GetRelativePath</c> on
///         netstandard2.0). What rides along are the two facts it was built from, so a reader of this value
///         is never left inferring them back out of the sentence.
///     </para>
/// </remarks>
internal sealed class NarrowedUniverse
{
    /// <summary>
    ///     Builds the narrowing from the filter that caused it, how much it left out, and the one-line
    ///     reason a rule it skipped reports.
    /// </summary>
    /// <param name="filterName">The <c>.slnf</c>'s file name — the file the operator passed.</param>
    /// <param name="uncheckedProjectCount">How many declared projects this run did not check.</param>
    /// <param name="ruleSkipReason">The one-line reason a rule skipped for narrowing carries.</param>
    internal NarrowedUniverse(string filterName, int uncheckedProjectCount, string ruleSkipReason)
    {
        FilterName = Guard.NotNull(filterName, nameof(filterName));
        UncheckedProjectCount = uncheckedProjectCount;
        RuleSkipReason = Guard.NotNull(ruleSkipReason, nameof(ruleSkipReason));
    }

    /// <summary>The <c>.slnf</c>'s file name.</summary>
    // Deliberately carried though nothing reads it yet (see the type remarks) — the same stance as
    // SolutionMembership.ReferencedSolutionPath, and the same sited suppression.
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    internal string FilterName { get; }

    /// <summary>The number of projects the solution declares that this run did not check; never zero.</summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    internal int UncheckedProjectCount { get; }

    /// <summary>
    ///     The one-line reason a narrowing-skipped rule reports. It names the filter and the count but not
    ///     the projects themselves: those ride the stamp above the report, one place to read them rather
    ///     than one copy per rule.
    /// </summary>
    internal string RuleSkipReason { get; }
}
