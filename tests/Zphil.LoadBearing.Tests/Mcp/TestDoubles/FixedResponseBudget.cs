using Zphil.LoadBearing.Cli.Mcp.Pipeline;

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
    public int MaxChars()
    {
        return maxChars;
    }
}
