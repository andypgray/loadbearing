using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The GRAMMAR §4.2 name-glob semantics, pinned verbatim: <c>*</c> matches any run including empty;
///     case-sensitive.
/// </summary>
public class TypeNamePatternTests
{
    [Theory]
    // Exact — no wildcard.
    [InlineData("Order", "Order", true)]
    [InlineData("Order", "Orders", false)]
    [InlineData("Order", "order", false)]
    // Suffix — leading '*' matches any run, including empty.
    [InlineData("*Repository", "OrderRepository", true)]
    [InlineData("*Repository", "Repository", true)]
    [InlineData("*Repository", "RepositoryFactoryX", false)]
    // Prefix — trailing '*'.
    [InlineData("I*", "IHandler", true)]
    [InlineData("I*", "I", true)]
    [InlineData("I*", "XI", false)]
    // Contains — bracketing wildcards.
    [InlineData("*Repo*", "Repo", true)]
    [InlineData("*Repo*", "OrderRepository", true)]
    [InlineData("*Repo*", "RepoManager", true)]
    [InlineData("*Repo*", "Rep", false)]
    // Multiple interior wildcards.
    [InlineData("*Order*Service", "MyOrderXService", true)]
    [InlineData("*Order*Service", "OrderService", true)]
    [InlineData("*Order*Service", "OrderServiceX", false)]
    // Lone '*' — everything.
    [InlineData("*", "Anything", true)]
    [InlineData("*", "", true)]
    public void Matches_TablePins(string pattern, string name, bool expected)
    {
        new TypeNamePattern(pattern).Matches(name).ShouldBe(expected);
    }

    [Fact]
    public void Matches_IsCaseSensitive()
    {
        new TypeNamePattern("*Repo*").Matches("orderrepository").ShouldBeFalse();
    }
}