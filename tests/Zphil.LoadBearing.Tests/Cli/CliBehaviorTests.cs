using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The fast CLI paths that need no workspace: an unknown command is remapped from System.CommandLine's
///     default exit 1 to 2, and the <c>mcp</c> command exits 2 rather than starting a hanging stdio server
///     when its solution binding cannot be resolved. The latter is a regression guard: <c>mcp --bogus</c>
///     is <em>not</em> a parse error — System.CommandLine swallows <c>--bogus</c> as the positional
///     solution argument — so the mcp action runs and must fail fast on the unresolvable bind. Before that
///     fail-fast existed, a redirected-but-never-closed stdin under the test host hung the whole suite.
/// </summary>
public sealed class CliBehaviorTests
{
    [Fact]
    public async Task UnknownCommand_ExitsTwo()
    {
        CliResult result = await CliRunner.InvokeAsync("frobnicate");

        result.Exit.ShouldBe(2);
    }

    [Fact]
    public async Task UnknownOption_ExitsTwo()
    {
        CliResult result = await CliRunner.InvokeAsync("check", "--bogus");

        result.Exit.ShouldBe(2);
    }

    [Fact]
    public async Task Mcp_BogusBinding_ExitsTwoWithoutStartingServer()
    {
        // `--bogus` is swallowed as the (unresolvable) positional solution, so the mcp action runs;
        // McpServerCommand must resolve the binding and exit 2 BEFORE starting the stdio server. If it
        // ever starts the server here, stdin never closes under the test host and the whole suite hangs.
        CliResult result = await CliRunner.InvokeAsync("mcp", "--bogus");

        result.Exit.ShouldBe(2);
    }
}