using System.Diagnostics;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

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

    /// <summary>
    ///     What a polluted environment would cost both shapes: discovery walks parents to the drive root, so
    ///     a stray solution in any ancestor of the temp root would bind the server and quietly void the test.
    /// </summary>
    private const string StrayWouldBind = "would bind this server — clean it.";

    /// <summary>
    ///     Shorter than the harness default on purpose, and the only budget this suite names: discovery
    ///     refuses before any workspace opens, so two minutes is already generous.
    /// </summary>
    private static readonly TimeSpan DiscoveryRefusalBudget = TimeSpan.FromMinutes(2);

    private readonly TempDirectory _temp = TestTempRoot.Fresh("unbound-server");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public async Task SeveralSolutionsAtTheRoot_StartsUnboundAndSaysWhyOnBothChannels()
    {
        // The ILSpy / MathNet shape: enough .sln* at the repository root that the walk-up cannot choose.
        SolutionPaths.ShouldHaveNoSolutionInAnyAncestor(_temp.Path, StrayWouldBind);
        SolutionPaths.CreateSln(_temp.Path, "Alpha.sln");
        SolutionPaths.CreateSln(_temp.Path, "Beta.slnf");
        SolutionPaths.CreateSln(_temp.Path, "Gamma.slnx");

        Conversation conversation = await ConverseAsync();

        conversation.Instructions.ShouldContain("not bound to a solution");
        conversation.Instructions.ShouldContain("Multiple solution files found");
        conversation.Instructions.ShouldContain("Alpha.sln");
        conversation.Instructions.ShouldContain("Gamma.slnx");
        // The filter is a candidate discovery already demoted, so the client is told about the two files a
        // choice actually stands between. Naming it would offer a third answer that resolves nothing.
        conversation.Instructions.ShouldNotContain("Beta.slnf");
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
        SolutionPaths.ShouldHaveNoSolutionInAnyAncestor(_temp.Path, StrayWouldBind);
        Directory.CreateDirectory(_temp.PathOf("src"));
        SolutionPaths.CreateSln(_temp.PathOf("src"), "Storefront.sln");

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
    ///     Launches the bare <c>mcp</c> child in the prepared root, completes the handshake, calls
    ///     <c>arch_graph</c>, and returns both halves. Asserts the two regressions that matter here inline —
    ///     that <c>initialize</c> answers at all, and that the tool call comes back — because a null
    ///     response is "the server died again" and deserves to say so with the child's stderr attached.
    /// </summary>
    private async Task<Conversation> ConverseAsync()
    {
        ProcessStartInfo startInfo = McpChildHarness.ServerStartInfo(TestsBinCli.Dll(), _temp.Path);

        ChildConversation conversation = await McpChildHarness.ConverseAsync(
            startInfo, [("arch_graph", 2, GraphRequest)], callBudget: DiscoveryRefusalBudget);

        conversation.Handshake.ShouldNotBeNull(
            "the MCP server never answered `initialize`. An unresolvable walk-up must not kill the server: "
            + "a process that exits before the handshake reaches the client as \"failed to start\" and no "
            + $"reason at all.\nstderr:\n{conversation.Diagnostics}");
        string? response = conversation.Answers.GetValueOrDefault("arch_graph");
        response.ShouldNotBeNull($"the arch_graph response never arrived.\nstderr:\n{conversation.Diagnostics}");

        return new Conversation(
            McpChildHarness.ShouldHaveInstructions(conversation.Handshake),
            McpChildHarness.ShouldHaveToolText(response, "arch_graph"),
            McpChildHarness.IsToolError(response));
    }

    private sealed record Conversation(string Instructions, string ToolText, bool ToolIsError);
}
