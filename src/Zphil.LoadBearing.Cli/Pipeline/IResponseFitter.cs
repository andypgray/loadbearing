namespace Zphil.LoadBearing.Cli.Pipeline;

/// <summary>
///     Picks which rung of a document's coarsening ladder a caller actually gets. The seam between a runner,
///     which knows how to compose its document at every grain, and a transport, which knows what will fit.
/// </summary>
/// <remarks>
///     <para>
///         It exists so a response budget stops being an input to a <em>run</em>. The budget is a property of
///         the caller's channel, not of the question asked, and threading it through a request record made
///         every CLI parse path pass a null it had to explain. A runner now offers its ladder and a fitter
///         decides; the CLI's fitter takes the first rung and the MCP server's walks down until one fits.
///     </para>
///     <para>
///         The ladder arrives lazily and must be consumed lazily: composing a rung costs a full serialization
///         of the document, so a fitter that materializes the whole ladder pays for coarser answers nobody
///         reads. Every ladder yields at least its floor, so <see cref="Fit" /> always has an answer.
///     </para>
/// </remarks>
internal interface IResponseFitter
{
    /// <summary>
    ///     Returns the document to write, given the rungs in coarsening order (finest first).
    /// </summary>
    /// <param name="ladder">
    ///     The composed documents, coarsest last and lazily produced — enumerate no further than needed.
    /// </param>
    string Fit(IEnumerable<string> ladder);
}

/// <summary>
///     The CLI's fitter: take the grain the caller asked for and never degrade. A terminal has no response
///     budget, so the first rung is the answer — and being a fitter rather than a null branch is what keeps
///     one code path through every runner's render.
/// </summary>
internal sealed class ResponseFitter : IResponseFitter
{
    /// <summary>The shared instance — it holds no state, and the default every runner falls back to.</summary>
    public static readonly IResponseFitter FirstRung = new ResponseFitter();

    private ResponseFitter()
    {
    }

    /// <inheritdoc />
    public string Fit(IEnumerable<string> ladder)
    {
        return ladder.First();
    }
}
