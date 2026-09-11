using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     The fact that this run's model is smaller than the codebase because part of it never loaded — what
///     turns a selection that matched nothing from a spec defect into a selection whose types or edges were
///     never extracted (GRAMMAR §4.1).
/// </summary>
/// <remarks>
///     <para>
///         Null on every run whose projects all loaded and all restored, so a run over a whole model reaches
///         exactly the advice it always did. It is the fact about the model rather than about the exit code,
///         so it is present whether or not the operator opted into checking the partial model: the hint is
///         true either way, and the surface that opted in is the one surface where the verdict still prints.
///     </para>
///     <para>
///         The sentence arrives composed rather than assembled here, for
///         <see cref="NarrowedUniverse" />'s two reasons: the load-failure lexicon lives with the stamps
///         every verb writes, so a wording minted in Core would drift from them silently; and Core cannot
///         relativize or name a project path anyway (no <c>Path.GetRelativePath</c> on netstandard2.0). The
///         failed and unrestored projects are therefore the composer's inputs rather than this value's
///         contents: what a checked rule needs is the sentence.
///     </para>
/// </remarks>
internal sealed class IncompleteModel
{
    /// <summary>Builds the fact from the one-line cure a rule whose selection matched nothing reports.</summary>
    /// <param name="emptySelectionHint">
    ///     The cure an empty subject or an inert target carries in place of its shape advice.
    /// </param>
    internal IncompleteModel(string emptySelectionHint)
    {
        EmptySelectionHint = Guard.NotNull(emptySelectionHint, nameof(emptySelectionHint));
    }

    /// <summary>
    ///     The cure a selection that matched nothing reports while the model is partial. It <em>replaces</em>
    ///     the shape advice rather than trailing it: advice to check a selection against what the solution
    ///     declares is wrong when the reason it came up empty may be that a project never loaded, and a
    ///     sentence carrying both would read as two cures for one diagnosis. It names the failed projects by
    ///     count and the repair, never the paths — those ride the stamp above the report, one place to read
    ///     them rather than one copy per rule, which is <see cref="NarrowedUniverse.RuleSkipReason" />'s
    ///     reasoning for the same shape.
    /// </summary>
    internal string EmptySelectionHint { get; }
}
