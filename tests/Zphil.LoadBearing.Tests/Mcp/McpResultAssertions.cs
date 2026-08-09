using ModelContextProtocol.Protocol;
using Shouldly;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The Shouldly surface over MCP results: asserts a result carries its one block of text and hands
///     the text back, failing by name — the block that is not text, or the result that carries none —
///     rather than by cast.
/// </summary>
/// <remarks>
///     Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///     <see cref="Zphil.LoadBearing.Tests.Checking.RuleResultAssertions" />.
/// </remarks>
internal static class McpResultAssertions
{
    /// <summary>Asserts the tool result has exactly one content block, a text one, and returns its text.</summary>
    internal static string ShouldHaveTextContent(this CallToolResult result)
    {
        return result.Content.ShouldHaveSingleItem().ShouldBeOfType<TextContentBlock>().Text;
    }

    /// <summary>Asserts the prompt result has at least one message, text-first, and returns that text.</summary>
    internal static string ShouldHaveTextContent(this GetPromptResult result)
    {
        result.Messages.ShouldNotBeEmpty();
        return result.Messages[0].Content.ShouldBeOfType<TextContentBlock>().Text;
    }
}
