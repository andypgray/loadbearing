using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The forgiving-input coercers for enum-shaped tool parameters:
///     <see cref="EnumValidationConverterFactory" /> (scalar) and <see cref="EnumArrayCoercerFactory" />
///     (array). Both refuse an unknown value with the full valid-values list — the message exists so a
///     model can self-correct on the next call instead of guessing against a byte-position error — and
///     both refuse integers, because an ordinal is not a name and admitting one lets a caller address
///     enum members positionally.
/// </summary>
/// <remarks>
///     <para>
///         Driven through <see cref="ToolInputSerializerOptions.Instance" />, the very options
///         <c>CoercingToolRegistration</c> hands the argument binder.
///     </para>
///     <para>
///         Neither has a live consumer: no <c>arch_*</c> parameter is enum-typed today, so nothing
///         reaches these converters through a real <c>tools/call</c>. They ship registered and ready for
///         the first enum-shaped parameter, and this is the only harness that reaches them — which is
///         also why the ordinals below are chosen so <c>Suggestion | Warning == Error</c>, the one
///         arrangement under which a name-list can impersonate a different member.
///     </para>
/// </remarks>
public sealed class EnumCoercionTests
{
    private const string ValidValues = "Hint, Suggestion, Warning, Error";

    private static readonly JsonSerializerOptions Options = ToolInputSerializerOptions.Instance;

    /// <summary>
    ///     Stand-in for a future enum-typed tool parameter. Ordinals are deliberately consecutive from
    ///     zero so <c>Suggestion | Warning == Error</c> — the arrangement a name-list needs to
    ///     impersonate a member nobody named. Public because xunit <c>InlineData</c> feeds it to public
    ///     test methods.
    /// </summary>
    public enum Severity
    {
        Hint = 0,
        Suggestion = 1,
        Warning = 2,
        Error = 3
    }

    [Theory]
    [InlineData("\"Warning\"", Severity.Warning)]
    [InlineData("\"Hint\"", Severity.Hint)]
    [InlineData("\"warning\"", Severity.Warning)]
    [InlineData("\"WARNING\"", Severity.Warning)]
    public void Scalar_KnownName_ParsesCaseInsensitively(string json, Severity expected)
    {
        Deserialize<Severity>(json)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("\"HIGH\"", "HIGH")]
    [InlineData("\"\"", "")]
    [InlineData("\"Warnings\"", "Warnings")]
    public void Scalar_UnknownName_RefusesWithTheFullValidValuesList(string json, string attempted)
    {
        var error = Should.Throw<UserErrorException>(() => Deserialize<Severity>(json));

        error.Message.ShouldBe($"Invalid value \"{attempted}\" for parameter. Valid values: {ValidValues}.");
    }

    [Theory]
    [InlineData("\"2\"")]
    [InlineData("\"+2\"")]
    [InlineData("\" 2 \"")]
    [InlineData("\"-1\"")]
    [InlineData("\"9223372036854775808\"")]
    public void Scalar_NumericString_IsRefusedRatherThanBoundToAnOrdinal(string json)
    {
        // Enum.TryParse would happily bind "2" to Warning. Admitting that would make the ordinal part of
        // the contract, so every member added or reordered later would silently change a caller's meaning.
        // The last case is wider than long, which is why the guard needs BigInteger and not just long.
        Should.Throw<UserErrorException>(() => Deserialize<Severity>(json))
            .Message.ShouldContain($"Valid values: {ValidValues}.");
    }

    [Theory]
    [InlineData("2", "Number")]
    [InlineData("true", "True")]
    [InlineData("{}", "StartObject")]
    [InlineData("[]", "StartArray")]
    public void Scalar_NonStringToken_RefusesWithTheSameValidValuesMessage(string json, string tokenKind)
    {
        // The advertised schema is "type": "string", so a non-string is already a contract violation;
        // the error surface stays uniform rather than splitting into two kinds of complaint.
        var error = Should.Throw<UserErrorException>(() => Deserialize<Severity>(json));

        error.Message.ShouldBe($"Invalid value \"{tokenKind}\" for parameter. Valid values: {ValidValues}.");
    }

    [Fact]
    public void Scalar_Write_EmitsTheMemberName()
    {
        JsonSerializer.Serialize(Severity.Warning, Options)
            .ShouldBe("\"Warning\"");
    }

    [Fact]
    public void Array_JsonArrayOfNames_ParsesEachElement()
    {
        Deserialize<Severity[]>("[\"Warning\",\"Error\"]")
            .ShouldBe([Severity.Warning, Severity.Error]);
    }

    [Fact]
    public void Array_EmptyJsonArray_ReadsAsEmpty()
    {
        Deserialize<Severity[]>("[]")
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\"[\\\"Warning\\\",\\\"Error\\\"]\"")]
    [InlineData("\"  [\\\"warning\\\", \\\"ERROR\\\"]  \"")]
    public void Array_JsonEncodedArrayString_IsUnwrappedAndEachElementParsed(string json)
    {
        Deserialize<Severity[]>(json)
            .ShouldBe([Severity.Warning, Severity.Error]);
    }

    [Fact]
    public void Array_BareName_BecomesASingleElementArray()
    {
        Deserialize<Severity[]>("\"Warning\"")
            .ShouldBe([Severity.Warning]);
    }

    [Theory]
    [InlineData("[\"Warning\",\"HIGH\"]", "HIGH")]
    [InlineData("\"HIGH\"", "HIGH")]
    [InlineData("\"[\\\"Warning\\\",\\\"HIGH\\\"]\"", "HIGH")]
    public void Array_UnknownNameAnywhere_RefusesWithTheFullValidValuesList(string json, string attempted)
    {
        // Including inside a stringified array: the unwrap path must not swallow the element error and
        // fall back to coercing the whole string, which would trade a precise message for a vague one.
        var error = Should.Throw<UserErrorException>(() => Deserialize<Severity[]>(json));

        error.Message.ShouldBe($"Invalid value \"{attempted}\" for parameter. Valid values: {ValidValues}.");
    }

    [Theory]
    [InlineData("[\"2\"]")]
    [InlineData("\"2\"")]
    [InlineData("\"[\\\"2\\\"]\"")]
    public void Array_NumericElement_IsRefusedRatherThanBoundToAnOrdinal(string json)
    {
        Should.Throw<UserErrorException>(() => Deserialize<Severity[]>(json))
            .Message.ShouldContain($"Valid values: {ValidValues}.");
    }

    [Theory]
    [InlineData("[2]", "Number")]
    [InlineData("[null]", "Null")]
    [InlineData("[true]", "True")]
    [InlineData("[[\"Warning\"]]", "StartArray")]
    public void Array_ElementIsNotAString_RefusesNamingTheElementTokenKind(string json, string tokenKind)
    {
        var error = Should.Throw<UserErrorException>(() => Deserialize<Severity[]>(json));

        error.Message.ShouldBe($"Expected a JSON array of Severity; got element of type {tokenKind}.");
    }

    [Theory]
    [InlineData("2", "Number")]
    [InlineData("true", "True")]
    [InlineData("{}", "StartObject")]
    public void Array_NonStringNonArrayToken_RefusesNamingTheTokenKind(string json, string tokenKind)
    {
        var error = Should.Throw<UserErrorException>(() => Deserialize<Severity[]>(json));

        error.Message.ShouldBe($"Expected a JSON array of Severity; got {tokenKind}.");
    }

    [Theory]
    [InlineData("\"[Warning, Error]\"")]
    [InlineData("\"[1,2]\"")]
    public void Array_StringLooksLikeAnArrayButIsNotOneOfStrings_FallsBackToParsingItWholeAndRefuses(
        string json)
    {
        // The string[] sibling keeps such a value verbatim as one element; here there is no such escape,
        // because the whole string is not an enum name either. The caller gets the valid-values list.
        Should.Throw<UserErrorException>(() => Deserialize<Severity[]>(json))
            .Message.ShouldContain($"Valid values: {ValidValues}.");
    }

    [Fact]
    public void Array_Write_EmitsMemberNamesAsAJsonArray()
    {
        JsonSerializer.Serialize(new[] { Severity.Warning, Severity.Error }, Options)
            .ShouldBe("[\"Warning\",\"Error\"]");
    }

    [Fact]
    public void Factories_ConvertTheirOwnShapeAndNothingElse()
    {
        EnumValidationConverterFactory scalar = new();
        scalar.CanConvert(typeof(Severity))
            .ShouldBeTrue();
        scalar.CanConvert(typeof(Severity[]))
            .ShouldBeFalse();
        scalar.CanConvert(typeof(string))
            .ShouldBeFalse();

        EnumArrayCoercerFactory array = new();
        array.CanConvert(typeof(Severity[]))
            .ShouldBeTrue();
        array.CanConvert(typeof(Severity))
            .ShouldBeFalse();
        array.CanConvert(typeof(string[]))
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("2", true)]
    [InlineData("+2", true)]
    [InlineData(" 2 ", true)]
    [InlineData("-1", true)]
    [InlineData("9223372036854775808", true)]
    [InlineData("Warning", false)]
    [InlineData("", false)]
    [InlineData("2a", false)]
    [InlineData("1e5", false)]
    [InlineData("0x2", false)]
    public void LooksNumeric_RecognisesEveryIntegerInDisguiseAndNothingElse(string value, bool expected)
    {
        // A leading-digit check would miss "+2" and " 2 ", and a long-only check would miss ordinals
        // wider than long.
        EnumStringHelper.LooksNumeric(value)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("2", true)]
    [InlineData("9223372036854775808", true)]
    [InlineData("Suggestion, Warning", true)]
    [InlineData("Suggestion,Warning", true)]
    [InlineData(",", true)]
    [InlineData("Warning", false)]
    [InlineData("Warning Error", false)]
    [InlineData("", false)]
    public void ResolvesByArithmetic_CatchesBothRoutesToAMemberNobodyNamed(string value, bool expected)
    {
        // The shared guard both converters call before Enum.TryParse. Ordinals are one route; a comma
        // list is the other, and a comma is decisive on its own because no member name can contain one.
        EnumStringHelper.ResolvesByArithmetic(value)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("\"Suggestion, Warning\"")]
    [InlineData("\"Suggestion,Warning\"")]
    public void Scalar_CommaSeparatedNameList_IsRefusedRatherThanCombinedIntoAnotherMember(string json)
    {
        // Enum.TryParse accepts a comma-separated list for ANY enum, not just [Flags], and ORs the
        // values: "Suggestion, Warning" is 1|2 == 3, which IS a defined member here (Error). So the
        // Enum.IsDefined guard passes and the caller silently gets a member they never named — the same
        // ordinal-arithmetic smuggling the numeric guard exists to stop, through a second door.
        Should.Throw<UserErrorException>(() => Deserialize<Severity>(json))
            .Message.ShouldContain($"Valid values: {ValidValues}.");
    }

    [Fact]
    public void Array_CommaSeparatedNameList_IsRefusedRatherThanCombinedIntoAnotherMember()
    {
        Should.Throw<UserErrorException>(() => Deserialize<Severity[]>("[\"Suggestion, Warning\"]"))
            .Message.ShouldContain($"Valid values: {ValidValues}.");
    }

    private static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
