using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The forgiving-input coercer for boolean tool parameters, <see cref="BoolCoercerFactory" />. It pins
///     the token shapes the class remarks promise to forgive and — just as load-bearing — the shapes it
///     promises to refuse: a flag coercer that guessed at <c>"1"</c> or <c>"yes"</c> would be inventing a
///     caller's intent, and the value it invented is one a caller can never see it invent.
/// </summary>
/// <remarks>
///     <para>
///         Driven through <see cref="ToolInputSerializerOptions.Instance" />, the very options
///         <c>CoercingToolRegistration</c> hands the argument binder, so these tests exercise the
///         production converter stack rather than a re-registration of it.
///     </para>
///     <para>
///         Unlike its string siblings this one has three live consumers on day one:
///         <c>arch_graph</c>'s <c>allowWorkspaceDiagnostics</c>, <c>overview</c> and <c>skeleton</c>.
///         <c>{"overview": "true"}</c> is the likeliest mis-spelling of a flag and, before this coercer,
///         the one input that reached the client as a raw byte-position deserializer error logged as a
///         server bug — see <c>GlobalCallToolFilterTests</c> for that row end to end.
///     </para>
/// </remarks>
public sealed class BoolCoercionTests
{
    private static readonly JsonSerializerOptions Options = ToolInputSerializerOptions.Instance;

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void RealBoolean_PassesThrough(string json, bool expected)
    {
        Deserialize<bool>(json)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("\"true\"", true)]
    [InlineData("\"false\"", false)]
    // bool.TryParse semantics: case-insensitive, surrounding whitespace tolerated.
    [InlineData("\"True\"", true)]
    [InlineData("\"FALSE\"", false)]
    [InlineData("\" TRUE \"", true)]
    public void StringSpellingOfABoolean_Coerces(string json, bool expected)
    {
        Deserialize<bool>(json)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("[true]", true)]
    [InlineData("[\"true\"]", true)]
    [InlineData("[false]", false)]
    [InlineData("[\"false\"]", false)]
    public void SingleElementArray_UnwrapsToTheBoolean(string json, bool expected)
    {
        // The shape models actually send when a scalar is advertised — forgiven identically to the
        // scalar-string coercer's ["A"].
        Deserialize<bool>(json)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("\"yes\"", "yes")]
    [InlineData("\"no\"", "no")]
    [InlineData("\"1\"", "1")]
    [InlineData("\"0\"", "0")]
    [InlineData("\"on\"", "on")]
    [InlineData("\"\"", "")]
    public void StringThatIsNotABooleanSpelling_RefusesNamingTheValue(string json, string value)
    {
        // Naming the value back is what makes the message self-correcting: the model sees what it sent
        // rather than a byte offset into a frame it never assembled.
        var error = Should.Throw<UserErrorException>(() => Deserialize<bool>(json));

        error.Message.ShouldBe($"Expected true or false; got \"{value}\".");
    }

    [Theory]
    [InlineData("42", "Number")]
    // 1 and 0 are the other tempting guess, and refused for the same reason as "1": a caller that meant
    // true has a spelling for it.
    [InlineData("1", "Number")]
    [InlineData("0", "Number")]
    [InlineData("null", "Null")]
    [InlineData("{}", "StartObject")]
    public void NonBooleanToken_RefusesNamingTheTokenKind(string json, string tokenKind)
    {
        var error = Should.Throw<UserErrorException>(() => Deserialize<bool>(json));

        error.Message.ShouldBe($"Expected true or false; got {tokenKind}.");
    }

    [Fact]
    public void EmptyArray_Refuses()
    {
        // The one place this coercer cannot mirror its string sibling: [] reads as "absent" there and
        // coerces to null, but a non-nullable bool has no null to land on, so guessing would mean
        // inventing false.
        var error = Should.Throw<UserErrorException>(() => Deserialize<bool>("[]"));

        error.Message.ShouldBe("Expected true or false; got an empty array.");
    }

    [Fact]
    public void MultiElementArray_RefusesAndAsksForAScalar()
    {
        var error = Should.Throw<UserErrorException>(() => Deserialize<bool>("[true,false]"));

        error.Message.ShouldContain("multiple elements");
        error.Message.ShouldContain("Pass a scalar boolean, not an array.");
    }

    [Theory]
    [InlineData("[1]", "Number")]
    [InlineData("[null]", "Null")]
    [InlineData("[[true]]", "StartArray")]
    [InlineData("[{}]", "StartObject")]
    public void ArrayElementIsNotABoolean_RefusesNamingTheElementTokenKind(string json, string tokenKind)
    {
        var error = Should.Throw<UserErrorException>(() => Deserialize<bool>(json));

        error.Message.ShouldBe($"Expected true or false; got array element of type {tokenKind}.");
    }

    [Fact]
    public void ArrayElementIsNotABooleanSpelling_RefusesNamingTheElementValue()
    {
        // Saying "array element" is the difference between the caller learning the coercer looked inside
        // their array and them reading it as though the array itself were the problem.
        var error = Should.Throw<UserErrorException>(() => Deserialize<bool>("[\"yes\"]"));

        error.Message.ShouldBe("Expected true or false; got array element \"yes\".");
    }

    [Fact]
    public void Write_EmitsTheBooleanUnchanged()
    {
        // The converter is registered for writing too — a coercer that mangled output would corrupt every
        // serialized boolean on these options, not just tool inputs.
        JsonSerializer.Serialize(true, Options)
            .ShouldBe("true");
        JsonSerializer.Serialize(false, Options)
            .ShouldBe("false");
    }

    [Fact]
    public void Factory_ConvertsItsOwnShapeAndNothingElse()
    {
        BoolCoercerFactory factory = new();
        factory.CanConvert(typeof(bool))
            .ShouldBeTrue();
        factory.CanConvert(typeof(bool[]))
            .ShouldBeFalse();
        factory.CanConvert(typeof(string))
            .ShouldBeFalse();
        factory.CanConvert(typeof(int))
            .ShouldBeFalse();
    }

    [Fact]
    public void NullableBool_IsServedByTheSameConverter()
    {
        // The factory does not claim bool? itself: STJ wraps the registered value-type converter for
        // Nullable<T> and answers the null on its own. Pinned because the first optional bool parameter
        // would otherwise quietly bind through the SDK default.
        Deserialize<bool?>("\"true\"")
            .ShouldBe(true);
        Deserialize<bool?>("null")
            .ShouldBeNull();
    }

    private static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
