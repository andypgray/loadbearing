using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     <see cref="Refusals" />'s stringified-array half: the reader that decides a value is a JSON array a
///     client wrote into a string parameter, and the two advice builders that answer it. Each refusal site is
///     pinned end to end beside its own verb; what is pinned here is the boundary between a shape refusal and
///     a roster, and the boundary between an echo and none, which no e2e case can enumerate.
/// </summary>
public sealed class RefusalsTests
{
    [Theory]
    [InlineData("[\"a\"]", new[] { "a" })]
    [InlineData("[\"a\",\"b\"]", new[] { "a", "b" })]
    [InlineData("['a']", new[] { "a" })]
    [InlineData("[a, b]", new[] { "a", "b" })]
    public void StringifiedArrayElements_ABracketedValue_ReadsItsElements(string value, string[] expected)
    {
        // What varies between clients is the quoting and the spacing, never the brackets — so all four
        // spellings have to reach the same elements for the advice to echo a value worth pasting back.
        Refusals.StringifiedArrayElements(value)
            .ShouldBe(expected);
    }

    [Fact]
    public void StringifiedArrayElements_TheEmptyArray_IsStillAnArray()
    {
        // Empty, not null: an array with nothing in it is refused on its shape like every other one, rather
        // than falling through to a roster of every name the caller did not ask for.
        Refusals.StringifiedArrayElements("[]")
            .ShouldNotBeNull()
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData("layering/*")]
    [InlineData("MyApp.Api")]
    [InlineData("")]
    public void StringifiedArrayElements_AValueWithoutBrackets_IsNotOne(string value)
    {
        // A rule ID is lowercase dash-and-slash segments and a project name carries dots, so the reader can
        // be this literal: nothing legitimate opens with '['.
        Refusals.StringifiedArrayElements(value)
            .ShouldBeNull();
    }

    [Fact]
    public void StringifiedArrayMessage_OverAGlobList_NamesTheShapeAndCarriesNoRoster()
    {
        var lead = "No rule matched '[\"a\",\"b\"]'";
        string advice = Refusals.GlobListAdvice(["a", "b"]);

        Refusals.StringifiedArrayMessage(lead, advice)
            .ShouldBe(
                "No rule matched '[\"a\",\"b\"]'. That is a JSON array written as text; pass the globs as one "
                + "semicolon-separated string: 'a;b'.");
    }

    [Fact]
    public void GlobListAdvice_NoElements_OmitsTheEcho()
    {
        // An empty quoted string is not a value anyone can paste back, so the empty array gets the advice
        // without one rather than an echo of nothing.
        Refusals.GlobListAdvice([])
            .ShouldBe("pass the globs as one semicolon-separated string");
    }

    [Fact]
    public void SingleValueAdvice_OneElement_EchoesItBesideTheCallersNoun()
    {
        // The echo is the whole value of the shape refusal for a scalar parameter: the caller's own bracket
        // already holds the value they meant, so the retry is a paste rather than a lookup.
        Refusals.SingleValueAdvice("pass one path", ["src/Foo"])
            .ShouldBe("pass one path: 'src/Foo'");
    }

    [Fact]
    public void SingleValueAdvice_SeveralElements_OmitsTheEcho()
    {
        // An array of several holds no single value the verb could have taken, so picking one for the caller
        // would be a guess dressed as advice.
        Refusals.SingleValueAdvice("pass one rule ID", ["a", "b"])
            .ShouldBe("pass one rule ID");
    }

    [Fact]
    public void SingleValueAdvice_NoElements_OmitsTheEcho()
    {
        // The empty array falls the same way as several, for the opposite reason: nothing to echo at all.
        Refusals.SingleValueAdvice("pass one rule ID", [])
            .ShouldBe("pass one rule ID");
    }
}
