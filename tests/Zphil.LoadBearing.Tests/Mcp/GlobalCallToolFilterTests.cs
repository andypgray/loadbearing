using System.Text.Json;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     Drives a real MCP client against the server over in-memory pipes to lock down
///     <see cref="Zphil.LoadBearing.Cli.Mcp.Pipeline.GlobalCallToolFilter" />'s branches — silent user-error, logged
///     unexpected-error, truncated success, unknown-parameter guard — end to end (acceptance box
///     2). Most rows ride the <c>arch_explain</c> DLL fast path (a built-DLL spec resolves with no
///     workspace), so the whole stack is proven in milliseconds; the truncation rows must call
///     <c>arch_graph</c>, because the narrowing hint is keyed on the tool name and only a real survey
///     proves it travels, and the bool-coercer row calls it as the only tool declaring a flag — that one
///     fails at binding, ahead of any survey. Serialized with the watchdog suites: the
///     filter brackets each call with the shared
///     <see cref="Zphil.LoadBearing.Cli.Mcp.Infrastructure.IdleTimeoutWatchdog" />
///     in-flight counter, so it must not run concurrently with the tests that read/reset that static.
/// </summary>
[Collection("Serial")]
public sealed class GlobalCallToolFilterTests
{
    [Fact]
    public async Task CallTool_UserError_ReturnsErrorResultAndLogsNothing()
    {
        // Arrange — arch_explain of an unknown rule ID throws UserErrorException on the DLL fast path.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = "nope/nope" },
            cancellationToken: Ct);

        // Assert — surfaced as an error result with the exact message, and the filter stayed silent.
        result.IsError.ShouldBe(true);
        string text = result.ShouldHaveTextContent();
        text.ShouldStartWith("Unknown rule ID 'nope/nope'.");
        // A bound server pays nothing for the unbound path: no recovery advice, because there is nothing
        // here to recover from.
        text.ShouldNotContain(ServerInstructions.UnboundCallCoda);
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_UserErrorWhileUnbound_AppendsTheSessionRecovery()
    {
        // Arrange — a harness flagged unbound over a solution that does resolve. Deliberate, and exactly the
        // filter's contract: it reads the nullness of the binding failure and nothing else, and the real
        // unbound launch cannot run in-process at all (McpUnboundServerTests drives that in a child).
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll),
            Ct,
            bindingFailure: "No .sln, .slnf or .slnx file found.");

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = "nope/nope" },
            cancellationToken: Ct);

        // Assert — the error still leads with what went wrong, and closes with what the reader can do about
        // it. ShouldEndWith is the placement pin: appended, never interleaved, never ahead of the reason.
        result.IsError.ShouldBe(true);
        string text = result.ShouldHaveTextContent();
        text.ShouldStartWith("Unknown rule ID 'nope/nope'.");
        text.ShouldEndWith(ServerInstructions.UnboundCallCoda);
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_UnknownParameterWhileUnbound_AlsoCarriesTheRecovery()
    {
        // Arrange — the pre-dispatch guard, which fails before discovery is ever reached. Pinning the coda
        // here is what makes the blanket semantics deliberate rather than incidental: fixing the argument
        // key would only surface the discovery refusal next, so the advice is true of this call too.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll),
            Ct,
            bindingFailure: "No .sln, .slnf or .slnx file found.");

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["rule"] = "layering/domain-independent" },
            cancellationToken: Ct);

        // Assert
        result.IsError.ShouldBe(true);
        string text = result.ShouldHaveTextContent();
        text.ShouldContain("\"rule\"");
        text.ShouldEndWith(ServerInstructions.UnboundCallCoda);
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_UnexpectedError_LogsExactlyOneWarningNamingTheTool()
    {
        // Arrange — a garbage .dll spec throws BadImageFormatException on load: not a user error. A file with
        // a .dll name but non-PE bytes is what SpecResolver's fast path finds, and what the ALC then rejects.
        using TempDirectory temp = TestTempRoot.Fresh("garbage-spec");
        string garbageDll = temp.PathOf("not-really.dll");
        File.WriteAllText(garbageDll, "this is not a portable executable");

        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, garbageDll), Ct);

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

    [Fact]
    public async Task CallTool_SuccessOverBudget_TruncatesAndLogsNothing()
    {
        // Arrange — a 10-token budget (25-char cap) forces truncation of a real explain body.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);
        harness.Environment.SetVariable("MAX_MCP_OUTPUT_TOKENS", "10");

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = "layering/domain-independent" },
            cancellationToken: Ct);

        // Assert — a successful result, truncated, unlogged.
        result.IsError.ShouldNotBe(true);
        result.ShouldHaveTextContent()
            .ShouldContain("--- RESPONSE TRUNCATED ---");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_GraphOverBudgetAtEveryGrain_TruncatesWithTheNarrowingHint()
    {
        // Arrange — a 10-token budget (25-char cap) no survey can fit at any grain, so arch_graph walks the
        // whole ladder down to its floor and the truncator still fires. That is the backstop case, and the
        // one that has to teach: with grain exhausted, the footer names the knob that is actually left. It
        // takes a budget this absurd to reach here now — the floor rung is a project roster, which grows
        // with the solution rather than the codebase.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);
        harness.Environment.SetVariable("MAX_MCP_OUTPUT_TOKENS", "10");

        // Act
        CallToolResult result = await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct);

        // Assert — the hint travels the whole pipeline, keyed on the tool name the filter passes through, and
        // points at scope rather than back down a ladder this survey has already reached the bottom of.
        result.IsError.ShouldNotBe(true);
        string text = result.ShouldHaveTextContent();
        text.ShouldContain("--- RESPONSE TRUNCATED ---");
        text.ShouldContain("Narrow the subject");
        text.ShouldContain("loadbearing graph --projects <globs> --json");
        text.ShouldNotContain("overview: true");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_GraphOverBudgetButFittingAtACoarserGrain_ReturnsAWholeDocument()
    {
        // Arrange — the case the ladder exists for, driven end to end through the real pipeline rather than
        // against the runner alone. A budget between the skeleton's size and the full survey's used to come
        // back cut, because the runner stepped once to overview and the filter then truncated that document
        // at the same number it had just been measured against.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);
        harness.Environment.SetVariable("MAX_MCP_OUTPUT_TOKENS", "400"); // 1000-char cap

        // Act
        CallToolResult result = await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct);

        // Assert — a whole, parseable survey, stamped with the grain it landed on.
        result.IsError.ShouldNotBe(true);
        string text = result.ShouldHaveTextContent();
        text.ShouldNotContain("--- RESPONSE TRUNCATED ---");
        using JsonDocument document = JsonDocument.Parse(text);
        document.RootElement.GetProperty("grain")
            .GetString()
            .ShouldBe("skeleton");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_UnparseableBool_ReturnsCoercerErrorAndLogsNothing()
    {
        // Arrange — "yes" for a flag is the mis-spelling BoolCoercerFactory exists for. Before it, Web
        // defaults read numbers from strings but never booleans, so this threw a raw JsonException:
        // CliErrorMapper does not recognise one as a user error, so the filter classified it as a bug and
        // returned a byte position. Binding fails ahead of dispatch, so no workspace is ever opened.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_graph",
            new Dictionary<string, object?> { ["overview"] = "yes" },
            cancellationToken: Ct);

        // Assert — the coercer's actionable message surfaces as an error, unlogged.
        result.IsError.ShouldBe(true);
        result.ShouldHaveTextContent()
            .ShouldContain("Expected true or false");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_UnknownParameterKey_ReturnsGuardErrorAndLogsNothing()
    {
        // Arrange — "rule" is the classic typo of the "ruleId" parameter; the guard fires pre-dispatch.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["rule"] = "layering/domain-independent" },
            cancellationToken: Ct);

        // Assert — the guard's actionable message surfaces as an error, unlogged.
        result.IsError.ShouldBe(true);
        string text = result.ShouldHaveTextContent();
        text.ShouldContain("\"rule\"");
        text.ShouldContain("arch_explain");
        harness.Logs.Warnings.ShouldBeEmpty();
    }
}
