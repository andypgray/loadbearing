using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests;

/// <summary>The GRAMMAR §4.2 namespace-pattern table, pinned verbatim (every match and non-match).</summary>
public class NamespacePatternTests
{
    [Theory]
    // MyApp.Domain.* — self-inclusive subtree.
    [InlineData("MyApp.Domain.*", "MyApp.Domain", true)]
    [InlineData("MyApp.Domain.*", "MyApp.Domain.Orders", true)]
    [InlineData("MyApp.Domain.*", "MyApp.Domain.Orders.Internal", true)]
    [InlineData("MyApp.Domain.*", "MyApp.DomainX", false)]
    [InlineData("MyApp.Domain.*", "MyApp", false)]
    // MyApp.Domain — exact.
    [InlineData("MyApp.Domain", "MyApp.Domain", true)]
    [InlineData("MyApp.Domain", "MyApp.Domain.Orders", false)]
    // MyApp.*.Orders — interior single-segment wildcard.
    [InlineData("MyApp.*.Orders", "MyApp.Sales.Orders", true)]
    [InlineData("MyApp.*.Orders", "MyApp.Orders", false)]
    [InlineData("MyApp.*.Orders", "MyApp.A.B.Orders", false)]
    // MyApp.Legacy* — partial-segment wildcard, never crosses a dot.
    [InlineData("MyApp.Legacy*", "MyApp.Legacy", true)]
    [InlineData("MyApp.Legacy*", "MyApp.LegacyBilling", true)]
    [InlineData("MyApp.Legacy*", "MyApp.Legacy.Billing", false)]
    // * — everything.
    [InlineData("*", "MyApp", true)]
    [InlineData("*", "MyApp.Domain.Orders", true)]
    public void Matches_TablePins(string pattern, string @namespace, bool expected)
    {
        new NamespacePattern(pattern).Matches(@namespace).ShouldBe(expected);
    }

    [Fact]
    public void Matches_IsCaseSensitive()
    {
        new NamespacePattern("MyApp.Domain").Matches("myapp.domain").ShouldBeFalse();
    }
}