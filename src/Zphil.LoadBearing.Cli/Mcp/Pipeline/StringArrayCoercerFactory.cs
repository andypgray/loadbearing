using System.Text.Json;
using System.Text.Json.Serialization;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     Coerces malformed <c>string[]</c> tool inputs into the array the caller clearly intended.
///     Models routinely send a JSON-encoded string (<c>"[\"A\",\"B\"]"</c>) where an array is
///     advertised, and one bare string (<c>"A"</c>) where a single-element array is expected;
///     the SDK default surfaces both as a generic byte-position deserializer error that gives
///     the model nothing actionable and burns retries.
/// </summary>
/// <remarks>
///     A real array reads normally, and a string whose contents parse as a JSON array of strings
///     returns the unwrapped array, surrounding whitespace tolerated. Any other string — the empty
///     string and near-array strings that are not valid JSON arrays of strings included — returns a
///     single-element array holding it verbatim, because a bare string where an array is advertised
///     is the caller's likeliest intent rather than an error. Any other token throws
///     <see cref="UserErrorException" /> naming the offending token kind.
/// </remarks>
internal sealed class StringArrayCoercerFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(string[]);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new StringArrayCoercer();
    }

    private sealed class StringArrayCoercer : JsonConverter<string[]>
    {
        public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartArray:
                    return CoercedJsonArray.ReadArray(ref reader, Identity, BadElement);

                case JsonTokenType.String:
                    string value = reader.GetString()!;
                    if (CoercedJsonArray.TryParseJsonStringArray(value, Identity, out string[] unwrapped)) return unwrapped;

                    return [value];

                default:
                    throw new UserErrorException(
                        $"Expected a JSON array of strings (e.g. [\"X\",\"Y\"]); got {reader.TokenType}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (string element in value) writer.WriteStringValue(element);

            writer.WriteEndArray();
        }

        // A string[] element is the string itself: the projection CoercedJsonArray takes is the identity,
        // which is the whole of what this coercer does differently from its enum sibling.
        private static string Identity(string element)
        {
            return element;
        }

        private static UserErrorException BadElement(JsonTokenType tokenType)
        {
            return new UserErrorException($"Expected a JSON array of strings; got element of type {tokenType}.");
        }
    }
}
