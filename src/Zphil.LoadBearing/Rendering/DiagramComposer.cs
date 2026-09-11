using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Composes the whole body <c>loadbearing render --diagram</c> writes: the codebase survey
///     (<see cref="GraphDiagramRenderer" />) and then the architecture the spec declares
///     (<see cref="LawDiagramRenderer" />), each with its own caption and its own fence. The two fences
///     answer different questions from different sources, what the code does and what the spec allows,
///     and a reader compares them without leaving the page.
/// </summary>
// The single seam the `render` command and the committed-artifact drift gate both go through, for
// the reason ContextFileComposer exists: a gate that rebuilds the body its own way can only prove
// that two pieces of code agree with each other, and the day they stop agreeing is the day the gate
// stops meaning anything.
public static class DiagramComposer
{
    /// <summary>
    ///     The managed-block body for a diagram file: the survey fence, a blank line, then the fence drawn
    ///     from the spec.
    /// </summary>
    /// <param name="summary">The codebase survey the first fence draws.</param>
    /// <param name="solutionName">The solution file's name, named in the survey's caption and title.</param>
    /// <param name="model">The model the second fence draws.</param>
    /// <param name="specName">The spec assembly's name, named in the second fence's caption and title.</param>
    /// <param name="scope">
    ///     The project filter, which reaches the survey alone. The second fence is drawn from the spec
    ///     rather than from the project graph, so a project-name filter has nothing to say about it, and a
    ///     narrowed survey can leave that fence naming places the survey above it dropped.
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
