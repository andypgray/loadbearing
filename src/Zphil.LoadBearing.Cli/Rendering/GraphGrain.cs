namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     How much detail a <c>graph</c> survey carries. A ladder, not a set of independent switches: each rung
///     keeps everything the one below it keeps and elides one more section, so a coarser answer is always a
///     subset of a finer one and a reader can compare two surveys by their stamp alone.
/// </summary>
/// <remarks>
///     <para>
///         Grain is never scope. Every rung surveys the same projects — <c>--projects</c> is the knob that
///         narrows the subject — which is what makes an automatic degrade safe: the answer stays about the
///         codebase the caller asked about, only in less detail.
///     </para>
///     <para>
///         The rungs are ordered by size for exactly one reason: a caller with a response budget walks down
///         until a whole document fits, rather than handing the truncator a document to cut in half. The
///         sections were chosen by measurement — on a 34-project solution the namespace inventories are the
///         first bulk and the external-reference rows are the largest single section by a wide margin.
///     </para>
/// </remarks>
internal enum GraphGrain
{
    /// <summary>Everything: namespace inventories, project edges and external-reference rows.</summary>
    Full = 0,

    /// <summary>Every project, edge and external row kept; each project's namespace inventory elided.</summary>
    Overview = 1,

    /// <summary>
    ///     The structural spine only: projects with their declared references and type counts, plus the
    ///     observed project edges. Namespace inventories and the external-reference rows are both elided, the
    ///     latter replaced by its count so the reader knows what is missing and how much of it there was.
    /// </summary>
    Skeleton = 2
}
