using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Cli.Pipeline;

namespace Zphil.LoadBearing.Tests.Mcp.TestDoubles;

/// <summary>
///     An <see cref="IResponseBudget" /> pinned to a character count, so a ladder test can name the exact
///     boundary it is testing.
/// </summary>
/// <remarks>
///     The production budget arrives as a token count and is multiplied out, which cannot express "one
///     character below the skeleton document" — the boundary the ladder's floor is pinned at. Driving
///     characters directly keeps those tests about the ladder rather than about the token conversion, which
///     <c>ResponseTruncatorTests</c> pins on its own.
/// </remarks>
internal sealed class FixedResponseBudget(int maxChars) : IResponseBudget
{
    /// <summary>
    ///     The MCP server's fitter at a budget named in characters — the form every ladder row reasons in,
    ///     for the reason given above.
    /// </summary>
    internal static IResponseFitter Fitter(int maxChars)
    {
        return new BudgetedResponseFitter(new FixedResponseBudget(maxChars));
    }

    public int MaxChars()
    {
        return maxChars;
    }
}
