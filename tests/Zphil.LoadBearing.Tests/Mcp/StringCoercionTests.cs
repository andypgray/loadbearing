using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The forgiving-input coercers for string-shaped tool parameters:
///     <see cref="StringCoercerFactory" /> (scalar) and <see cref="StringArrayCoercerFactory" /> (array).
///     Each pins the token shapes its class remarks promise to forgive, and — just as load-bearing — the
///     shapes it promises to refuse, because a coercer that guesses too eagerly corrupts a valid argument
///     rather than rejecting an invalid one.
/// </summary>
/// <remarks>
///     <para>
///         Driven through <see cref="ToolInputSerializerOptions.Instance" />, the very options
///         <c>CoercingToolRegistration</c> hands the argument binder, so these tests exercise the
///         production converter stack rather than a re-registration of it.
///     </para>
///     <para>
///         The array coercer has no live consumer: every <c>arch_*</c> parameter today is
///         <c>string</c>, <c>string?</c> or <c>bool</c>, so nothing reaches
///         <see cref="StringArrayCoercerFactory" /> through a real <c>tools/call</c>. It ships registered
///         and ready for the first array-shaped parameter, and this is the only harness that reaches it.
///     </para>
/// </remarks>
public sealed class StringCoercionTests
{
    private static readonly JsonSerializerOptions Options = ToolInputSerializerOptions.Instance;

    [Fact]
    public void Scalar_PlainString_PassesThroughVerbatim()
    {
        Deserialize<string>("\"layering/domain-independent\"").ShouldBe("layering/domain-independent");
    }

    [Theory]
    [InlineData("\"[A]\"", "[A]")]
    [InlineData("\"[\\\"A\\\"]\"", "[\"A\"]")]
    [InlineData("\"\"", "")]
    public void Scalar_StringThatLooksLikeAnArray_IsNotUnwrapped(string json, string expected)
    {
        // A literal string argument must survive untouched: unwrapping here would silently rewrite a
        // caller's real value, which is worse than the error it would be saving them from.
        Deserialize<string>(json).ShouldBe(expected);
    }

    [Fact]
    public void Scalar_SingleElementArray_UnwrapsToTheString()
    {
        // The shape models actually send when a scalar is advertised.
        Deserialize<string>("[\"arch_check\"]").ShouldBe("arch_check");
    }

    [Fact]
    public void Scalar_EmptyArray_CoercesToNull()
    {
        Deserialize<string>("[]").ShouldBeNull();
    }

    [Fact]
    public void Scalar_Null_IsNull()
    {
        Deserialize<string>("null").ShouldBeNull();
    }

    [Fact]
    public void Scalar_MultiElementArray_RefusesAndAsksForAScalar()
    {
        var error = Should.Throw<UserErrorException>(() => Deserialize<string>("[\"A\",\"B\"]"));

        error.Message.ShouldContain("multiple elements");
        error.Message.ShouldContain("Pass a scalar string, not an array.");
    }

    [Theory]
    [InlineData("[1]", "Number")]
    [InlineData("[null]", "Null")]
    [InlineData("[true]", "True")]
    [InlineData("[[\"A\"]]", "StartArray")]
    [InlineData("[{}]", "StartObject")]
    public void Scalar_ArrayElementIsNotAString_RefusesNamingTheElementTokenKind(string json, string tokenKind)
    {
        var error = Should.Throw<UserErrorException>(() => Deserialize<string>(json));

        error.Message.ShouldBe($"Expected a string; got array element of type {tokenKind}.");
    }

    [Theory]
    [InlineData("42", "Number")]
    [InlineData("true", "True")]
    [InlineData("false", "False")]
    [InlineData("{}", "StartObject")]
    public void Scalar_NonStringToken_RefusesNamingTheTokenKind(string json, string tokenKind)
    {
        var error = Should.Throw<UserErrorException>(() => Deserialize<string>(json));

        error.Message.ShouldBe($"Expected a string; got {tokenKind}.");
    }

    [Fact]
    public void Scalar_Write_EmitsTheStringAndNullUnchanged()
    {
        // The converter is registered for writing too — a coercer that mangles output would corrupt
        // every serialized string on these options, not just tool inputs.
        JsonSerializer.Serialize("A", Options).ShouldBe("\"A\"");
        JsonSerializer.Serialize((string?)null, Options).ShouldBe("null");
    }

    [Fact]
    public void Array_RealJsonArray_ReadsAsIs()
    {
        Deserialize<string[]>("[\"A\",\"B\"]").ShouldBe(["A", "B"]);
    }

    [Fact]
    public void Array_EmptyJsonArray_ReadsAsEmpty()
    {
        Deserialize<string[]>("[]").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\"[\\\"A\\\",\\\"B\\\"]\"")]
    [InlineData("\"  [\\\"A\\\",\\\"B\\\"]  \"")]
    [InlineData("\"[ \\\"A\\\" , \\\"B\\\" ]\"")]
    public void Array_JsonEncodedArrayString_IsUnwrapped(string json)
    {
        // The shape models actually send: the array, but stringified. Surrounding whitespace tolerated.
        Deserialize<string[]>(json).ShouldBe(["A", "B"]);
    }

    [Theory]
    [InlineData("\"A\"", "A")]
    [InlineData("\"\"", "")]
    [InlineData("\"   \"", "   ")]
    public void Array_BareString_BecomesASingleElementArray(string json, string expected)
    {
        Deserialize<string[]>(json).ShouldBe([expected]);
    }

    [Theory]
    [InlineData("\"[A, B]\"", "[A, B]")]
    [InlineData("\"[1,2]\"", "[1,2]")]
    [InlineData("\"[\\\"A\\\", 2]\"", "[\"A\", 2]")]
    [InlineData("\"[[\\\"A\\\"]]\"", "[[\"A\"]]")]
    [InlineData("\"[\\\"A\\\"] trailing\"", "[\"A\"] trailing")]
    public void Array_StringLooksLikeAnArrayButIsNotOneOfStrings_IsKeptVerbatimAsASingleElement(
        string json,
        string expected)
    {
        // Falling back to the verbatim string rather than throwing is the point: only a JSON array whose
        // every element is a string is unambiguous enough to unwrap. Anything else may be a real value.
        Deserialize<string[]>(json).ShouldBe([expected]);
    }

    [Fact]
    public void Array_JsonEncodedEmptyArrayString_UnwrapsToEmpty()
    {
        Deserialize<string[]>("\"[]\"").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("[\"A\",1]", "Number")]
    [InlineData("[\"A\",null]", "Null")]
    [InlineData("[\"A\",true]", "True")]
    [InlineData("[[\"A\"]]", "StartArray")]
    [InlineData("[{}]", "StartObject")]
    public void Array_ElementIsNotAString_RefusesNamingTheElementTokenKind(string json, string tokenKind)
    {
        var error = Should.Throw<UserErrorException>(() => Deserialize<string[]>(json));

        error.Message.ShouldBe($"Expected a JSON array of strings; got element of type {tokenKind}.");
    }

    [Theory]
    [InlineData("42", "Number")]
    [InlineData("true", "True")]
    [InlineData("{}", "StartObject")]
    public void Array_NonStringNonArrayToken_RefusesNamingTheTokenKindAndShowsTheShape(
        string json,
        string tokenKind)
    {
        var error = Should.Throw<UserErrorException>(() => Deserialize<string[]>(json));

        error.Message.ShouldBe($"Expected a JSON array of strings (e.g. [\"X\",\"Y\"]); got {tokenKind}.");
    }

    [Fact]
    public void Array_Write_EmitsAPlainJsonArrayOfStrings()
    {
        JsonSerializer.Serialize(new[] { "A", "B" }, Options).ShouldBe("[\"A\",\"B\"]");
        JsonSerializer.Serialize(Array.Empty<string>(), Options).ShouldBe("[]");
    }

    [Fact]
    public void Factories_ConvertTheirOwnShapeAndNothingElse()
    {
        StringCoercerFactory scalar = new();
        scalar.CanConvert(typeof(string)).ShouldBeTrue();
        scalar.CanConvert(typeof(string[])).ShouldBeFalse();
        scalar.CanConvert(typeof(int)).ShouldBeFalse();

        StringArrayCoercerFactory array = new();
        array.CanConvert(typeof(string[])).ShouldBeTrue();
        array.CanConvert(typeof(string)).ShouldBeFalse();
        array.CanConvert(typeof(List<string>)).ShouldBeFalse();
    }

    private static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
