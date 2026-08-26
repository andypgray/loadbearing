using Zphil.LoadBearing.Cli.Pipeline;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     How much detail a JSON document carries. A ladder, not a set of independent switches: each rung keeps
///     everything the one below it keeps and elides one more level of nesting, so a coarser answer is always
///     a subset of a finer one and a reader can compare two documents by their stamp alone.
/// </summary>
/// <remarks>
///     <para>
///         One vocabulary across every document that has a ladder, so a caller learns the rung names once and
///         the two surfaces cannot spell them apart: <c>graph</c> coarsens its survey and <c>check</c> its
///         report against the same enum, the same wire names and the same coarsest-flag-wins mapping. What a
///         rung elides is each renderer's own to say; that it is strictly coarser than the rung below is this
///         type's.
///     </para>
///     <para>
///         Grain is never scope. Every rung answers about the same subject — <c>--projects</c> and
///         <c>--rules</c> are the knobs that narrow that — which is what makes an automatic degrade safe: the
///         answer stays about the codebase the caller asked about, only in less detail.
///     </para>
///     <para>
///         The rungs are ordered by size for exactly one reason: a caller with a response budget walks down
///         until a whole document fits, rather than handing the truncator a document to cut in half. The
///         elided levels were chosen by measurement — the bulk of each document is the array that scales with
///         the codebase rather than with the spec.
///     </para>
///     <para>
///         <see cref="Index" /> is where that reasoning ends: every rung above it still carries something
///         that scales with the codebase, and it carries nothing that does. What survives it is the roster —
///         every project name, every rule id — which scales with the authored and structural dimension
///         alone, so no realistic solution reaches the truncator through it. That is what makes the floor
///         rung the narrowing menu as well as the smallest answer: the arguments <c>--projects</c> and
///         <c>--rules</c> take are exactly what it lists.
///     </para>
/// </remarks>
internal enum DocumentGrain
{
    /// <summary>
    ///     Everything. In the survey: namespace inventories, project edges and external-reference rows. In
    ///     the report: rules, their violations, and every violation's sites.
    /// </summary>
    Full = 0,

    /// <summary>
    ///     One level elided. In the survey: every project, edge and external row kept, each project's
    ///     namespace inventory gone. In the report: every rule and violation kept, each violation's sites
    ///     replaced by their count.
    /// </summary>
    Overview = 1,

    /// <summary>
    ///     The spine only. In the survey: projects with their declared references and type counts, plus the
    ///     observed project edges; namespace inventories and the external-reference rows are both elided, the
    ///     latter replaced by its count. In the report: every rule with its prose and verdict, its violations
    ///     replaced by their count.
    /// </summary>
    Skeleton = 2,

    /// <summary>
    ///     The roster only — the ladder's floor, and the narrowing menu. In the survey: every project by
    ///     name, with its solution membership and type counts, its observed edges replaced by their count;
    ///     declared references and frameworks go with the rest. In the report: every rule by id, with its
    ///     posture, verdict, baseline, warnings and violation count; the prose goes. Both keep every trust
    ///     stamp and every roll-up, because those bound the answer rather than fill it.
    /// </summary>
    Index = 3
}

/// <summary>
///     The three things every laddered document does with a <see cref="DocumentGrain" />: read one off the
///     caller's flags, walk the ladder from it, and spell it on the wire. Sited here rather than in each
///     renderer so the CLI and the MCP surface cannot drift on any of them — a survey and a report stamped
///     <c>overview</c> mean the same rung, and both walk down to the same coarsest one.
/// </summary>
internal static class DocumentGrains
{
    /// <summary>
    ///     The grain a caller's three flags name. The coarsest wins: they are a floor on detail rather than
    ///     competing modes, so passing several asks for the coarsest one instead of being an error worth
    ///     refusing over.
    /// </summary>
    public static DocumentGrain Coarsest(bool overview, bool skeleton, bool index)
    {
        if (index) return DocumentGrain.Index;
        if (skeleton) return DocumentGrain.Skeleton;

        return overview ? DocumentGrain.Overview : DocumentGrain.Full;
    }

    /// <summary>
    ///     Every rung from <paramref name="floor" /> down to the coarsest, each document composed by
    ///     <paramref name="compose" /> — what a runner offers an <see cref="IResponseFitter" />.
    /// </summary>
    /// <remarks>
    ///     Lazy on purpose: each rung costs a full serialization of the document, so a fitter that stops at
    ///     the first pays for exactly one. The ladder cannot spin — every rung is strictly coarser than the
    ///     one before it, and <see cref="DocumentGrain.Index" /> is last. This bound is the only place the
    ///     last rung is named: every other comparison in the product is a <c>&gt;=</c> against the rung whose
    ///     elision it guards, so a new rung below reaches them all unedited.
    /// </remarks>
    /// <param name="floor">The grain the caller asked for: the finest rung offered, and always yielded.</param>
    /// <param name="compose">Composes the whole document at one grain.</param>
    public static IEnumerable<string> Ladder(DocumentGrain floor, Func<DocumentGrain, string> compose)
    {
        for (DocumentGrain at = floor; at <= DocumentGrain.Index; at++) yield return compose(at);
    }

    /// <summary>
    ///     The wire spelling of a coarsened grain, or <see langword="null" /> at
    ///     <see cref="DocumentGrain.Full" /> — which omits the key, so a document that says nothing about
    ///     grain is the complete one.
    /// </summary>
    public static string? Wire(DocumentGrain grain)
    {
        return grain switch
        {
            DocumentGrain.Overview => "overview",
            DocumentGrain.Skeleton => "skeleton",
            DocumentGrain.Index => "index",
            _ => null
        };
    }
}
