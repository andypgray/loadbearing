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
    Skeleton = 2
}

/// <summary>
///     The two things every laddered document does with a <see cref="DocumentGrain" />: read one off the
///     caller's flags, and spell it on the wire. Sited here rather than in each renderer so the CLI and the
///     MCP surface cannot drift on either — a survey and a report stamped <c>overview</c> mean the same rung.
/// </summary>
internal static class DocumentGrains
{
    /// <summary>
    ///     The grain a caller's two flags name. The coarsest wins: they are a floor on detail rather than
    ///     competing modes, so passing both asks for the coarser one instead of being an error worth
    ///     refusing over.
    /// </summary>
    public static DocumentGrain Coarsest(bool overview, bool skeleton)
    {
        if (skeleton) return DocumentGrain.Skeleton;

        return overview ? DocumentGrain.Overview : DocumentGrain.Full;
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
            _ => null
        };
    }
}
