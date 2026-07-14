using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     Drives a real MCP client against the server over in-memory pipes to lock down
///     <see cref="Cli.Mcp.Pipeline.GlobalCallToolFilter" />'s branches — silent user-error, logged
///     unexpected-error, truncated success, unknown-parameter guard — end to end (Phase 7 acceptance box
///     2). Every row rides the <c>arch_explain</c> DLL fast path (a built-DLL spec resolves with no
///     workspace), so the whole stack is proven in milliseconds. Serialized with the watchdog suites: the
///     filter brackets each call with the shared <see cref="Cli.Mcp.Infrastructure.IdleTimeoutWatchdog" />
///     in-flight counter, so it must not run concurrently with the tests that read/reset that static.
/// </summary>
[Collection("Serial")]
public sealed class GlobalCallToolFilterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CallTool_UserError_ReturnsErrorResultAndLogsNothing()
    {
        // Arrange — arch_explain of an unknown rule ID throws UserErrorException on the DLL fast path.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = "nope/nope" },
            cancellationToken: Ct);

        // Assert — surfaced as an error result with the exact message, and the filter stayed silent.
        result.IsError.ShouldBe(true);
        TextOf(result).ShouldStartWith("Unknown rule ID 'nope/nope'.");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_UnexpectedError_LogsExactlyOneWarningNamingTheTool()
    {
        // Arrange — a garbage .dll spec throws BadImageFormatException on load: not a user error.
        string garbageDll = WriteGarbageDll();
        try
        {
            await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
                Binding(CliRunner.MyAppSolution, garbageDll), Ct);

            // Act
            CallToolResult result = await harness.Client.CallToolAsync(
                "arch_explain",
                new Dictionary<string, object?> { ["ruleId"] = "any/thing" },
                cancellationToken: Ct);

            // Assert — surfaced as an error, and logged exactly once as a warning that names the tool.
            result.IsError.ShouldBe(true);
            LogEntry warning = harness.Logs.Warnings.ShouldHaveSingleItem();
            warning.Message.ShouldContain("arch_explain");
        }
        finally
        {
            File.Delete(garbageDll);
        }
    }

    [Fact]
    public async Task CallTool_SuccessOverBudget_TruncatesAndLogsNothing()
    {
        // Arrange — a 10-token budget (25-char cap) forces truncation of a real explain body.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);
        harness.Environment.SetVariable("MAX_MCP_OUTPUT_TOKENS", "10");

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = "layering/domain-independent" },
            cancellationToken: Ct);

        // Assert — a successful result, truncated, unlogged.
        result.IsError.ShouldNotBe(true);
        TextOf(result).ShouldContain("--- RESPONSE TRUNCATED ---");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_UnknownParameterKey_ReturnsGuardErrorAndLogsNothing()
    {
        // Arrange — "rule" is the classic typo of the "ruleId" parameter; the guard fires pre-dispatch.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["rule"] = "layering/domain-independent" },
            cancellationToken: Ct);

        // Assert — the guard's actionable message surfaces as an error, unlogged.
        result.IsError.ShouldBe(true);
        string text = TextOf(result);
        text.ShouldContain("\"rule\"");
        text.ShouldContain("arch_explain");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    private static McpServerBinding Binding(string? solution, string? spec)
    {
        string workingDirectory = solution is null
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(solution))!;
        return new McpServerBinding(solution, spec, workingDirectory);
    }

    private static string TextOf(CallToolResult result)
    {
        return ((TextContentBlock)result.Content.Single()).Text;
    }

    // A temp file with a .dll name but non-PE bytes — SpecResolver's fast path finds it, then the ALC
    // load throws BadImageFormatException (an unexpected error, not a UserErrorException).
    private static string WriteGarbageDll()
    {
        string path = Path.Combine(Path.GetTempPath(), $"loadbearing-garbage-{Guid.NewGuid():N}.dll");
        File.WriteAllText(path, "this is not a portable executable");
        return path;
    }
}