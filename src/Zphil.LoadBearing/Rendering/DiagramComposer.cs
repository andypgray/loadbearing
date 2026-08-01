using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Composes the whole <c>--diagram</c> managed block: the codebase survey
///     (<see cref="GraphDiagramRenderer" />) and then the architecture law
///     (<see cref="LawDiagramRenderer" />), each with its own caption and its own fence, in one body.
///     The two fences answer different questions from different sources — what the codebase does, and
///     what the spec says it may — and putting them in one artifact is the point: a reader compares them
///     without leaving the page.
/// </summary>
/// <remarks>
///     This is the single seam the <c>render</c> command and the committed-artifact drift gate both go
///     through, for the reason <see cref="ContextFileComposer" /> exists: a gate that rebuilds the body
///     its own way can only prove that two pieces of code agree with each other, and the day they stop
///     agreeing is the day the gate stops meaning anything.
/// </remarks>
public static class DiagramComposer
{
    /// <summary>The managed-block body for a diagram target.</summary>
    /// <param name="summary">The codebase survey the first fence draws.</param>
    /// <param name="solutionName">The solution file name, named in the survey's caption and title.</param>
    /// <param name="model">The reified spec the second fence draws.</param>
    /// <param name="specName">The spec assembly name, named in the law's caption and title.</param>
    /// <param name="scope">
    ///     The project filter, which reaches the survey only. The law fence is never scoped: it is drawn
    ///     from the spec rather than from the project graph, so a project-name filter has nothing to say
    ///     about it, and a narrowed survey can leave the law naming places the survey above it dropped.
    /// </param>
    public static string Compose(
        GraphSummary summary, string solutionName, ArchitectureModel model, string specName, DiagramScope? scope = null)
    {
        Guard.NotNull(summary, nameof(summary));
        Guard.NotNull(model, nameof(model));

        return GraphDiagramRenderer.Block(summary, solutionName, scope)
               + "\n\n"
               + LawDiagramRenderer.Block(model, specName);
    }
}