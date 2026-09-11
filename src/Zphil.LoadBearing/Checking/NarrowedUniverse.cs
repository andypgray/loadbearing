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
///         The reason arrives composed rather than assembled here: the narrowing lexicon lives with the
///         stamps every verb writes, so a wording minted in Core would drift from them silently; and
///         Core cannot relativize or name a project path anyway (no <c>Path.GetRelativePath</c> on
///         netstandard2.0). The filter's name and the count it left out are therefore the composer's
///         inputs rather than this value's contents: what a checked rule needs is the sentence.
///     </para>
/// </remarks>
internal sealed class NarrowedUniverse
{
    /// <summary>Builds the narrowing from the one-line reason a rule it skipped reports.</summary>
    /// <param name="ruleSkipReason">The one-line reason a rule skipped for narrowing carries.</param>
    internal NarrowedUniverse(string ruleSkipReason)
    {
        RuleSkipReason = Guard.NotNull(ruleSkipReason, nameof(ruleSkipReason));
    }

    /// <summary>
    ///     The one-line reason a narrowing-skipped rule reports. It names the filter and the count but not
    ///     the projects themselves: those ride the stamp above the report, one place to read them rather
    ///     than one copy per rule.
    /// </summary>
    internal string RuleSkipReason { get; }
}
