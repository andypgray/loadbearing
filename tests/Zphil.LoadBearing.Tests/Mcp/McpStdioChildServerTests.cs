using System.Diagnostics;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The deployment-shaped net under the MCP server: the shipped <c>loadbearing.dll</c> run as a real
///     child process, spoken to over real stdio, with the client's stdin pipe held open for the whole
///     call — exactly what an MCP client does, and a condition no in-process MCP suite here can
///     reproduce.
/// </summary>
/// <remarks>
///     <para>
///         <b>What this guards.</b> <c>arch_check</c> with a <c>diffBase</c> shells git. A child started
///         without its own redirected stdin inherits a duplicate of this server's, which under a live
///         client is the JSON-RPC pipe the stdio transport is permanently parked on in a synchronous
///         read; Git for Windows probes its standard handles at startup and that probe blocks forever
///         against such a pipe. The child never reached its own entry point, the 30-second ceiling
///         expired, and the tool call failed 100% of the time — while every test stayed green, because
///         <see cref="McpPipelineHarness" /> drives the server in-process over memory pipes and the CLI
///         inherits a stdin that is already at EOF. Only a real child over real stdio sees it, so this
///         test spawns one.
///     </para>
///     <para>
///         Hand-written JSON-RPC frames rather than an SDK client, because the condition under test
///         <em>is</em> the pipe: stdin is written, flushed, and deliberately never closed until the
///         assertions are done. The conversation and its budgets live in
///         <see cref="McpChildHarness.ConverseAsync" />, shared with the other real-child suites; the
///         defaults there cover a cold child — MSBuild registration, the vswhere probe, and a full
///         workspace load of the fixture solution — so a wedge shows up as the id:2 response never
///         arriving rather than as an indefinite hang.
///     </para>
///     <para>
///         <b>Why it survives beside the repo-handle suite.</b> That suite drives the same conversation
///         over the same plumbing, so the setups really are near-identical — but every one of its facts
///         opens with <c>Assert.SkipUnless(ProcessFileFootprint.IsSupported, …)</c>, and the footprint scan
///         is Windows-x64 only. CI runs ubuntu and macos legs too, and on those this is the <em>only</em>
///         real-child-over-real-stdio coverage there is. Deleting it would leave the stdin-inheritance
///         wedge unguarded on two of four legs.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class McpStdioChildServerTests
{
    private const string CheckWithDiffBaseRequest =
        """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"arch_check","arguments":{"diffBase":"HEAD"}}}""";

    [Fact]
    public async Task ArchCheckWithDiffBase_OverRealStdio_ReturnsTheReport()
    {
        using var repo = new TempGitRepo();
        // A brand-new untracked file in dragon territory, so the report can only carry the tripwire if git
        // actually ran and produced a diff — the assertion the wedge fails.
        File.WriteAllText(
            repo.PathOf("MyApp.Legacy.Billing", "LegacyNote.cs"),
            "namespace MyApp.Legacy.Billing;\n\npublic class LegacyNote;\n");

        ProcessStartInfo startInfo = McpChildHarness.ServerStartInfo(
            McpChildHarness.TestsBinCliDll(),
            repo.SolutionPath,
            CliRunner.QuarantinedSpecDll,
            repo.Root);

        ChildConversation conversation = await McpChildHarness.ConverseAsync(
            startInfo, [("arch_check", 2, CheckWithDiffBaseRequest)]);

        conversation.Handshake.ShouldNotBeNull(
            $"the MCP server never answered `initialize` over real stdio.\nstderr:\n{conversation.Diagnostics}");
        string? response = conversation.Answers.GetValueOrDefault("arch_check");
        response.ShouldNotBeNull(
            "the arch_check response never arrived. A child process that inherits the server's live stdin "
            + "pipe wedges at startup and the call cannot complete — the failure no in-process MCP test can "
            + $"see.\nstderr:\n{conversation.Diagnostics}");

        string text = McpChildHarness.ShouldHaveToolText(response, "arch_check");
        McpChildHarness.IsToolError(response)
            .ShouldBeFalse($"arch_check reported a tool error: {text}");

        // The tripwire proves the whole diff path ran: git resolved the toplevel, listed the untracked
        // file, and the checker matched it against the quarantined scope.
        text.ShouldContain("quarantinedScopeTouched");
    }
}
