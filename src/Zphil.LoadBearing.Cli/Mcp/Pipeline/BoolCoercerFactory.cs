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
///         Handled token shapes for any <c>bool</c> (or <c>bool?</c>) parameter:
///     </para>
///     <list type="bullet">
///         <item>
///             <description><c>True</c> / <c>False</c> → pass through.</description>
///         </item>
///         <item>
///             <description>
///                 <c>String</c> that <see cref="bool.TryParse(string,out bool)" /> accepts — <c>"true"</c>,
///                 <c>"False"</c>, <c>" TRUE "</c>: case-insensitive, surrounding whitespace tolerated →
///                 coerce. Nothing else is admitted, because <c>"1"</c>, <c>"yes"</c> and <c>"on"</c> are
///                 guesses at a caller's intent rather than spellings of a boolean.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>StartArray</c> holding a single element that itself coerces (<c>[true]</c>,
///                 <c>["true"]</c>) → unwrap. The same one-element-array slip
///                 <see cref="StringCoercerFactory" /> forgives.
///             </description>
///         </item>
///         <item>
///             <description>
///                 Anything else — an unparseable string, the empty array, an array with multiple elements,
///                 a number (<c>1</c> is not <c>true</c> here), <c>Null</c>, an object → throw
///                 <see cref="UserErrorException" /> naming the offending value or token kind. Note the
///                 empty array cannot mean "absent" the way it does for a string: a non-nullable
///                 <c>bool</c> has no null to fall back to.
///             </description>
///         </item>
///     </list>
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
