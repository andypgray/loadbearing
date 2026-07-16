using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;

namespace Zphil.LoadBearing.Tests.Baselines;

/// <summary>
///     Pins <see cref="SymbolIds.Display" />: the leading <c>DocumentationCommentId</c> tag — <c>T:</c>
///     for a type, <c>M:</c>/<c>P:</c>/<c>F:</c>/<c>E:</c> for a member (GRAMMAR §4.5) — stripped for
///     human output, everything else (the <c>unresolved:</c> fallback) verbatim.
/// </summary>
public sealed class SymbolIdsTests
{
    [Theory]
    [InlineData("T:MyApp.Web.HomeController", "MyApp.Web.HomeController")]
    [InlineData("P:System.DateTime.Now", "System.DateTime.Now")]
    [InlineData("M:N.T.M(System.Int32)", "N.T.M(System.Int32)")]
    [InlineData("F:N.Box.Value", "N.Box.Value")]
    [InlineData("E:N.Pub.Evt", "N.Pub.Evt")]
    public void Display_DocIdPrefix_StripsTheTag(string symbolId, string expected)
    {
        SymbolIds.Display(symbolId).ShouldBe(expected);
    }

    [Fact]
    public void Display_UnresolvedFallback_PrintsVerbatim()
    {
        SymbolIds.Display("unresolved:N.Thing.Member").ShouldBe("unresolved:N.Thing.Member");
    }
}