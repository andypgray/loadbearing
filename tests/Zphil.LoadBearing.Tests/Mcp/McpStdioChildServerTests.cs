using System.Diagnostics;
using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The deployment-shaped net under the MCP server: the shipped <c>loadbearing.dll</c> run as a real
///     child process, spoken to over real stdio, with the client's stdin pipe held open for the whole
///     call — exactly what an MCP client does, and exactly the condition every other MCP suite here
///     cannot reproduce.
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
///         Three hand-written JSON-RPC frames rather than an SDK client, because the condition under test
///         <em>is</em> the pipe: stdin is written, flushed, and deliberately never closed until the
///         assertions are done. The generous budgets cover a cold child — MSBuild registration, the
///         vswhere probe, and a full workspace load of the fixture solution — and a wedge shows up as the
///         id:2 response never arriving rather than as an indefinite hang. The plumbing itself lives in
///         <see cref="McpChildHarness" />, shared with <c>McpChildServerRepoHandleTests</c>.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class McpStdioChildServerTests
{
    private const string CheckWithDiffBaseRequest =
        """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"arch_check","arguments":{"diffBase":"HEAD"}}}""";

    /// <summary>Budget for the handshake: a cold child, MSBuild registration, and the vswhere probe.</summary>
    private static readonly TimeSpan HandshakeBudget = TimeSpan.FromMinutes(2);

    /// <summary>Budget for the call itself: a cold workspace load plus extraction plus the git diff.</summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromMinutes(5);

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

        string? handshake = null;
        string? response = null;
        string diagnostics;

        using (Process server = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Failed to start the MCP server child."))
        {
            // Drained from the start so a chatty child cannot fill its stderr pipe and stall.
            var errorDrain = server.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            try
            {
                await McpChildHarness.SendAsync(server, McpChildHarness.InitializeRequest);
                handshake = await McpChildHarness.ReadResponseAsync(server.StandardOutput, id: 1, HandshakeBudget);

                if (handshake is not null)
                {
                    await McpChildHarness.SendAsync(server, McpChildHarness.InitializedNotification);
                    await McpChildHarness.SendAsync(server, CheckWithDiffBaseRequest);
                    response = await McpChildHarness.ReadResponseAsync(server.StandardOutput, id: 2, CallBudget);
                }
            }
            catch (IOException)
            {
                // The child died mid-conversation and the pipe broke; the null results below say so, with
                // its stderr attached — a better failure than an IOException stack.
            }
            finally
            {
                // stdin stays open until here: an open client pipe is the condition under test.
                McpChildHarness.TryKillTree(server);
            }

            // Drained while the process object is still alive, so disposal cannot fault the read out from
            // under it. Asserted afterwards, so a failure message can carry the server's own diagnostics.
            diagnostics = await McpChildHarness.DrainAsync(errorDrain);
        }

        handshake.ShouldNotBeNull(
            $"the MCP server never answered `initialize` over real stdio.\nstderr:\n{diagnostics}");
        response.ShouldNotBeNull(
            "the arch_check response never arrived. A child process that inherits the server's live stdin "
            + "pipe wedges at startup and the call cannot complete — the failure no in-process MCP test can "
            + $"see.\nstderr:\n{diagnostics}");

        using JsonDocument document = JsonDocument.Parse(response);
        document.RootElement.TryGetProperty("error", out JsonElement error)
            .ShouldBeFalse($"the server returned a JSON-RPC error: {error}");

        JsonElement result = document.RootElement.GetProperty("result");
        string text = result.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;

        bool isError = result.TryGetProperty("isError", out JsonElement flag)
                       && flag.ValueKind == JsonValueKind.True;
        isError.ShouldBeFalse($"arch_check reported a tool error: {text}");

        // The tripwire proves the whole diff path ran: git resolved the toplevel, listed the untracked
        // file, and the checker matched it against the quarantined scope.
        text.ShouldContain("quarantinedScopeTouched");
    }
}
