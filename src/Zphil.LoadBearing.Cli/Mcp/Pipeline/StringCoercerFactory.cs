using System.Text.Json;
using System.Text.Json.Serialization;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     Coerces malformed scalar <c>string</c> tool inputs into the value the caller clearly
///     intended. Models routinely wrap a single value in a one-element array (<c>["A"]</c>)
///     where a scalar <c>string</c> is advertised; the SDK default surfaces this as a generic
///     byte-position deserializer error that gives the model nothing actionable and burns
///     retries. Symmetric counterpart to <see cref="StringArrayCoercerFactory" />.
/// </summary>
/// <remarks>
///     <para>
///         A string passes through verbatim — one that merely looks like an array (<c>"[A]"</c>) is
///         deliberately NOT unwrapped, because a literal string argument must survive untouched. A
///         one-element array unwraps to its string, and the empty array coerces to <c>null</c>, the
///         semantic match for "absent". Anything else — a longer array, a non-string element, any
///         other token — throws <see cref="UserErrorException" /> naming the offending token kind.
///     </para>
///     <para>
///         <see cref="JsonConverter{T}" /> on <c>string?</c> serves both <c>string</c> and
///         <c>string?</c> parameters: <c>string</c> is a reference type and
///         <see cref="System.Text.Json" /> resolves nullability at the binding layer, not the
///         converter layer.
///     </para>
/// </remarks>
internal sealed class StringCoercerFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(string);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new StringCoercer();
    }

    private sealed class StringCoercer : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;

                case JsonTokenType.StartArray:
                    return ScalarArrayUnwrap.Read(
                        ref reader,
                        ReadCoercedScalar,
                        static () => null,
                        "Expected a string; got an array with multiple elements. " +
                        "Pass a scalar string, not an array.");

                default:
                    return ReadCoercedScalar(ref reader, insideArray: false);
            }
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value is null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value);
        }

        /// <summary>
        ///     The scalar rules, shared by the top level and the single-element-array unwrap.
        ///     <paramref name="insideArray" /> chooses the wording only: an error naming an "array element"
        ///     tells the caller the coercer did look inside their array and still could not use what it
        ///     found there. <see cref="JsonTokenType.Null" /> is deliberately not admitted here — the top
        ///     level answers a bare null with "absent", which a null <em>inside</em> an array does not mean.
        /// </summary>
        private static string? ReadCoercedScalar(ref Utf8JsonReader reader, bool insideArray)
        {
            if (reader.TokenType == JsonTokenType.String) return reader.GetString();

            string tokenSubject = insideArray
                ? $"array element of type {reader.TokenType}"
                : reader.TokenType.ToString();
            throw new UserErrorException($"Expected a string; got {tokenSubject}.");
        }
    }
}
