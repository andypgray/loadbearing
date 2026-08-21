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

    /// <summary>
    ///     Ceiling on the unbound banner itself. Everything it spends is spent ahead of the same cliff, so
    ///     it is a direct deduction from how much of <see cref="ServerInstructions.Text" /> an unbound
    ///     server's client ever sees.
    /// </summary>
    /// <remarks>
    ///     Raised from 400 when the banner took on <see cref="ServerInstructions.UnboundCallCoda" />: an
    ///     unbound client buys a recovery it can act on with roughly 240 more characters of the file's tail,
    ///     which is the trade the file's ordering was designed to make payable. This is also the coda's only
    ///     budget — it is inside the banner, so bounding the banner bounds it.
    /// </remarks>
    private const int BannerBudget = 650;

    [Fact]
    public void Text_FitsUnderTheClientTruncationCliff()
    {
        ServerInstructions.Text.Length.ShouldBeLessThanOrEqualTo(ClientTruncationCliff);
    }

    [Fact]
    public void For_BoundServer_IsTheInstructionsUnchanged()
    {
        // Reference equality, not just equal text: a bound server must pay nothing at all for the
        // unbound path existing.
        ServerInstructions.For(null)
            .ShouldBeSameAs(ServerInstructions.Text);
    }

    [Fact]
    public void For_UnboundServer_LeadsWithTheReasonAndKeepsTheInstructionsWhole()
    {
        const string failure = "Multiple solution files found in C:\\repo: Alpha.sln, Beta.slnx.";

        string instructions = ServerInstructions.For(failure);

        instructions.ShouldStartWith("**This server is running but is not bound to a solution.**");
        // Verbatim: the discovery message names the files and the fix, and re-wording it here would
        // make the handshake and the per-call tool error disagree about what went wrong.
        instructions.ShouldContain(failure);
        // A prefix, never a replacement — the tool surface is described in the same words either way.
        instructions.ShouldEndWith(ServerInstructions.Text);
    }

    [Fact]
    public void For_UnboundServer_NamesBothWaysToNameTheSolution()
    {
        string instructions = ServerInstructions.For("No .sln, .slnf or .slnx file found.");

        instructions.ShouldContain("`args`");
        instructions.ShouldContain("LOADBEARING_SOLUTION_PATH");
    }

    [Fact]
    public void For_UnboundServer_PutsTheInSessionRecoveryBeforeTheConfigRemedies()
    {
        // The whole point of the change: a reader who stops early must meet the thing they can do, not the
        // two things they cannot. "x" keeps the discovery text out of it, so neither index can match inside
        // the quoted failure; Text carries neither marker, so neither can match below the banner either.
        string instructions = ServerInstructions.For("x");

        int recovery = instructions.IndexOf(ServerInstructions.UnboundCallCoda, StringComparison.Ordinal);
        int configRemedies = instructions.IndexOf("`args`", StringComparison.Ordinal);

        recovery.ShouldBeGreaterThanOrEqualTo(0);
        configRemedies.ShouldBeGreaterThan(recovery);
    }

    [Fact]
    public void For_UnboundServer_EmbedsTheCallCodaVerbatim()
    {
        // Verbatim, not merely equivalent: the banner and every tool-call error carry the same constant, so
        // the handshake channel and the per-call channel cannot drift into describing different recoveries.
        ServerInstructions.For("x")
            .ShouldContain(ServerInstructions.UnboundCallCoda);
    }

    [Fact]
    public void UnboundCallCoda_NamesARecoveryTheSessionCanPerform()
    {
        // Lowercase `loadbearing` is the command, not the product name Text spells LoadBearing — the
        // suite's case-sensitive ShouldContain is what keeps those two apart.
        ServerInstructions.UnboundCallCoda.ShouldContain("loadbearing graph <solution>");
        ServerInstructions.UnboundCallCoda.ShouldContain("loadbearing check <solution>");

        // And the negative half, which is the actual finding: a remedy an in-session reader cannot take
        // belongs below the one they can, never inside it. Both of these are client-config edits.
        ServerInstructions.UnboundCallCoda.ShouldNotContain("`args`");
        ServerInstructions.UnboundCallCoda.ShouldNotContain("LOADBEARING_SOLUTION_PATH");
    }

    [Fact]
    public void For_UnboundServer_KeepsTheRecoveryAboveTheTruncationCliff()
    {
        // A generous real failure: the multi-solution refusal on a big repository names candidate paths, so
        // it is the longest thing that can sit between the header and the recovery. Even at 600 characters
        // the recovery ends well clear of the cut, which is what "above the fold" has to mean here.
        string failure = new('x', 600);

        string instructions = ServerInstructions.For(failure);

        int recoveryEnd = instructions.IndexOf(ServerInstructions.UnboundCallCoda, StringComparison.Ordinal)
                          + ServerInstructions.UnboundCallCoda.Length;
        recoveryEnd.ShouldBeLessThanOrEqualTo(ClientTruncationCliff);
    }

    [Fact]
    public void For_UnboundServer_BannerStaysWithinItsBudget()
    {
        // The banner cannot fit under the cliff and leave Text whole — that trade is accepted, which is
        // why the file keeps its most droppable lines last. What must not happen silently is the banner
        // growing and pushing more of Text past the cut, so its own cost is pinned here.
        const string failure = "x";
        int bannerLength = ServerInstructions.For(failure)
            .Length - ServerInstructions.Text.Length - failure.Length;

        bannerLength.ShouldBeLessThanOrEqualTo(BannerBudget);
    }
}
