using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     Pins the embedded server instructions under the client's truncation cliff, so growth past it
///     turns up as a red here rather than as a silent cut in every session.
/// </summary>
/// <remarks>
///     Claude Code renders MCP server instructions into the session system prompt whole only up to
///     2,048 UTF-16 code units; anything longer is sliced there and suffixed "… [truncated]"
///     (measured 2026-08-06 against 2.1.220 — the constant and the slice are in the client binary,
///     and a live session showed the previous 4,272-character text cut at exactly character 2,048,
///     leaving 52% of the file invisible). <see cref="string.Length" /> counts the same units the
///     client slices, so this pin measures exactly what the client measures. When it reds, cut or
///     move content — tool descriptions are the per-fetch channel — never raise the budget. The
///     unbound banner (<see cref="ServerInstructions.For" />) prepends further text ahead of the same
///     cliff, which is why the file keeps its most droppable lines last.
/// </remarks>
public sealed class ServerInstructionsTests
{
    /// <summary>The observed Claude Code cliff: instructions longer than this are cut mid-text.</summary>
    private const int ClientTruncationCliff = 2048;

    [Fact]
    public void Text_FitsUnderTheClientTruncationCliff()
    {
        ServerInstructions.Text.Length.ShouldBeLessThanOrEqualTo(ClientTruncationCliff);
    }
}
