using System.Diagnostics;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The launch shape <c>.mcp/server.json</c> prescribes — the bare positional <c>mcp</c>, no solution
///     argument — over the two directory shapes a real repository presents when the walk-up cannot resolve
///     one: several solutions at the root, and none at the root with one under <c>src\</c>.
/// </summary>
/// <remarks>
///     <para>
///         <b>What this guards.</b> Both shapes used to kill the server during <c>initialize</c>. The
///         refusals were well written and named a fix, and <em>neither reached a client</em>: there is no
///         tool call in which a result could be returned before the handshake, and the message went to
///         stderr, which for the MCP surface is discarded by construction. What the client saw was a server
///         that failed to start, and nothing else. So the server now starts unbound and says why through
///         the two in-band channels a client does read — the <c>initialize</c> instructions, and every
///         tool call's error result, which re-runs the identical discovery.
///     </para>
///     <para>
///         <b>Why a real child.</b> The no-argument path no longer fails fast, so an in-process invocation
///         of it starts a server that never exits: the test host redirects stdin and never closes it. That
///         is the hang <c>CliBehaviorTests</c> records, and it is why nothing here may run in-process. The
///         plumbing is <see cref="McpChildHarness" />, shared with the other child suites; the frames are
///         hand-written for the same reason they are there.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class McpUnboundServerTests : IDisposable
{
    private const string GraphRequest =
        """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"arch_graph","arguments":{}}}""";

    /// <summary>Budget for the handshake: a cold child, MSBuild registration, and the vswhere probe.</summary>
    private static readonly TimeSpan HandshakeBudget = TimeSpan.FromMinutes(2);

    /// <summary>Budget for the call: discovery refuses before any workspace opens, so this is generous.</summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromMinutes(2);

    private readonly string _root = Directory.CreateTempSubdirectory("loadbearing-unbound-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task SeveralSolutionsAtTheRoot_StartsUnboundAndSaysWhyOnBothChannels()
    {
        // The ILSpy / MathNet shape: enough .sln* at the repository root that the walk-up cannot choose.
        AssertNoSolutionInAnyAncestor(_root);
        CreateSln("Alpha.sln");
        CreateSln("Beta.slnf");
        CreateSln("Gamma.slnx");

        Conversation conversation = await ConverseAsync();

        conversation.Instructions.ShouldContain("not bound to a solution");
        conversation.Instructions.ShouldContain("Multiple solution files found");
        conversation.Instructions.ShouldContain("Alpha.sln");
        conversation.Instructions.ShouldContain("Beta.slnf");
        conversation.Instructions.ShouldContain("Gamma.slnx");
        conversation.Instructions.ShouldContain("Pass the solution as the argument");
        // The banner is a prefix, never a replacement: the tool surface is described in the same words a
        // bound server describes it in.
        conversation.Instructions.ShouldContain("arch_graph");

        conversation.ToolIsError.ShouldBeTrue(conversation.ToolText);
        conversation.ToolText.ShouldContain("Multiple solution files found");
        conversation.ToolText.ShouldContain("Alpha.sln");
    }

    [Fact]
    public async Task NoSolutionAnywhere_StartsUnboundAndNamesTheOneOneLevelDown()
    {
        // The nopCommerce shape: nothing at the repository root, the solution under src\ — so the walk-up
        // climbs past the repository to the drive root and finds nothing at all.
        AssertNoSolutionInAnyAncestor(_root);
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "Storefront.sln"), "");

        Conversation conversation = await ConverseAsync();

        conversation.Instructions.ShouldContain("not bound to a solution");
        conversation.Instructions.ShouldContain("No .sln, .slnf or .slnx file found");
        // The near miss, named: this is what turns the dead end into one copy-paste.
        conversation.Instructions.ShouldContain(Path.Combine("src", "Storefront.sln"));
        conversation.Instructions.ShouldContain("Pass the solution as the argument");

        conversation.ToolIsError.ShouldBeTrue(conversation.ToolText);
        conversation.ToolText.ShouldContain("No .sln, .slnf or .slnx file found");
        conversation.ToolText.ShouldContain(Path.Combine("src", "Storefront.sln"));
    }

    /// <summary>
    ///     The precondition both shapes rest on: discovery walks parents to the drive root, so a stray
    ///     solution in <em>any</em> ancestor of the temp root would bind the server and quietly void the
    ///     test. Fail loudly on a polluted environment instead — including for the ambiguous shape, whose
    ///     walk-up does not stop at the first ambiguous directory but keeps climbing for a single one.
    /// </summary>
    private static void AssertNoSolutionInAnyAncestor(string directory)
    {
        for (DirectoryInfo? dir = new(directory); dir is not null; dir = dir.Parent)
        {
            string[] solutionFiles;
            try
            {
                solutionFiles = Directory.EnumerateFiles(dir.FullName, "*.sln")
                    .Concat(Directory.EnumerateFiles(dir.FullName, "*.slnf"))
                    .Concat(Directory.EnumerateFiles(dir.FullName, "*.slnx"))
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            solutionFiles.ShouldBeEmpty(
                $"Stray solution file under ancestor '{dir.FullName}' would bind this server — clean it.");
        }
    }

    private void CreateSln(string name)
    {
        File.WriteAllText(Path.Combine(_root, name), "");
    }

    /// <summary>
    ///     Launches the bare <c>mcp</c> child in the prepared root, completes the handshake, calls
    ///     <c>arch_graph</c>, and returns both halves. Asserts the two regressions that matter here inline —
    ///     that <c>initialize</c> answers at all, and that the tool call comes back — because a null
    ///     response is "the server died again" and deserves to say so with the child's stderr attached.
    /// </summary>
    private async Task<Conversation> ConverseAsync()
    {
        ProcessStartInfo startInfo = McpChildHarness.ServerStartInfo(McpChildHarness.TestsBinCliDll(), _root);

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
                    await McpChildHarness.SendAsync(server, GraphRequest);
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
                // stdin stays open until here: an open client pipe is what a real client holds.
                McpChildHarness.TryKillTree(server);
            }

            diagnostics = await McpChildHarness.DrainAsync(errorDrain);
        }

        handshake.ShouldNotBeNull(
            "the MCP server never answered `initialize`. An unresolvable walk-up must not kill the server: "
            + "a process that exits before the handshake reaches the client as \"failed to start\" and no "
            + $"reason at all.\nstderr:\n{diagnostics}");
        response.ShouldNotBeNull($"the arch_graph response never arrived.\nstderr:\n{diagnostics}");

        return new Conversation(InstructionsOf(handshake), ToolTextOf(response), ToolIsErrorOf(response));
    }

    private static string InstructionsOf(string handshake)
    {
        using JsonDocument document = JsonDocument.Parse(handshake);
        document.RootElement.TryGetProperty("error", out JsonElement error)
            .ShouldBeFalse($"the server returned a JSON-RPC error to initialize: {error}");

        return document.RootElement.GetProperty("result").GetProperty("instructions").GetString() ?? string.Empty;
    }

    private static string ToolTextOf(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        document.RootElement.TryGetProperty("error", out JsonElement error)
            .ShouldBeFalse($"the server returned a JSON-RPC error rather than a tool error: {error}");

        return document.RootElement.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString() ?? string.Empty;
    }

    private static bool ToolIsErrorOf(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        return document.RootElement.GetProperty("result").TryGetProperty("isError", out JsonElement flag)
               && flag.ValueKind == JsonValueKind.True;
    }

    private sealed record Conversation(string Instructions, string ToolText, bool ToolIsError);
}
