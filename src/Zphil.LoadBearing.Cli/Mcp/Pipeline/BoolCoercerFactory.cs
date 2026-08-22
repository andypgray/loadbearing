using System.Text.Json;
using System.Text.Json.Serialization;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     Coerces malformed <c>bool</c> tool inputs into the value the caller clearly intended. Models
///     routinely spell a flag as a string (<c>{"overview": "true"}</c>) where a boolean is advertised, and
///     <see cref="JsonSerializerDefaults.Web" /> reads numbers from strings but never booleans — so without
///     this the SDK raises a byte-position <see cref="JsonException" />, which
///     <c>CliErrorMapper.UserFacingMessage</c> does not recognise as a user error: the call is logged as a
///     bug and the model is handed a byte offset it can do nothing with. Scalar sibling of
///     <see cref="StringCoercerFactory" />.
/// </summary>
/// <remarks>
///     <para>
///         Admitted for any <c>bool</c> parameter: the boolean tokens; a string
///         <see cref="bool.TryParse(string,out bool)" /> accepts (case-insensitive, surrounding
///         whitespace tolerated — <c>"1"</c>, <c>"yes"</c> and <c>"on"</c> are guesses at a caller's
///         intent rather than spellings of a boolean, so they refuse); and a single-element array whose
///         element itself coerces, the same slip <see cref="StringCoercerFactory" /> forgives. Anything
///         else throws <see cref="UserErrorException" /> naming the offending value or token kind —
///         including the empty array, which cannot mean "absent" the way it does for a string, because a
///         non-nullable <c>bool</c> has no null to fall back to.
///     </para>
///     <para>
///         A <see cref="JsonConverter{T}" /> over <c>bool</c> serves a future <c>bool?</c> parameter too:
///         <see cref="System.Text.Json" /> wraps the registered value-type converter for
///         <see cref="Nullable{T}" /> and answers the <c>null</c> itself.
///     </para>
/// </remarks>
internal sealed class BoolCoercerFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(bool);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new BoolCoercer();
    }

    private sealed class BoolCoercer : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
                return ScalarArrayUnwrap.Read(
                    ref reader,
                    ReadCoercedScalar,
                    static () => throw new UserErrorException("Expected true or false; got an empty array."),
                    "Expected true or false; got an array with multiple elements. " +
                    "Pass a scalar boolean, not an array.");

            return ReadCoercedScalar(ref reader, insideArray: false);
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            writer.WriteBooleanValue(value);
        }

        /// <summary>
        ///     The scalar rules, shared by the top level and the single-element-array unwrap.
        ///     <paramref name="insideArray" /> chooses the wording only: an error naming an "array element"
        ///     tells the caller the coercer did look inside their array and still could not use what it
        ///     found there.
        /// </summary>
        private static bool ReadCoercedScalar(ref Utf8JsonReader reader, bool insideArray)
        {
            if (reader.TokenType == JsonTokenType.True) return true;

            if (reader.TokenType == JsonTokenType.False) return false;

            if (reader.TokenType != JsonTokenType.String)
            {
                string tokenSubject = insideArray
                    ? $"array element of type {reader.TokenType}"
                    : reader.TokenType.ToString();
                throw new UserErrorException($"Expected true or false; got {tokenSubject}.");
            }

            string text = reader.GetString()!;
            if (bool.TryParse(text, out bool parsed)) return parsed;

            string valueSubject = insideArray ? $"array element \"{text}\"" : $"\"{text}\"";
            throw new UserErrorException($"Expected true or false; got {valueSubject}.");
        }
    }
}
