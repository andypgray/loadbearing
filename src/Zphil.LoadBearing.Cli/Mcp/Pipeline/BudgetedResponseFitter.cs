using Zphil.LoadBearing.Cli.Pipeline;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     The MCP server's fitter: walk the ladder and answer with the first rung that fits the client's budget,
///     so an over-budget document comes back whole at a coarser grain instead of cut at a line boundary.
/// </summary>
/// <remarks>
///     <para>
///         It walks the whole ladder rather than stepping once, because one step is not enough on a real
///         codebase: on a 34-project solution the full survey is ~147k characters and the overview it
///         degrades to is still ~82k, over any default budget. Stopping there hands
///         <see cref="ResponseTruncator" /> exactly the document this class exists to avoid producing.
///     </para>
///     <para>
///         Past the last rung it returns that rung anyway. The ladder is a ladder with a last step, not a
///         guarantee — a document still over budget at its coarsest grain lands on the truncator like any
///         other response, and the footer there names the knob that narrows the <em>subject</em>, which is
///         all that is left once the grain ladder is exhausted.
///     </para>
///     <para>
///         The budget is read once per <see cref="Fit" /> — that is, once per tool call — and the rungs are
///         pulled one at a time, so a document that fits at the grain the caller asked for costs exactly one
///         serialization.
///     </para>
/// </remarks>
internal sealed class BudgetedResponseFitter(IResponseBudget budget) : IResponseFitter
{
    /// <inheritdoc />
    public string Fit(IEnumerable<string> ladder)
    {
        int maxChars = budget.MaxChars();
        var coarsest = string.Empty;

        foreach (string rung in ladder)
        {
            coarsest = rung;
            if (rung.Length <= maxChars) return rung;
        }

        return coarsest;
    }
}
