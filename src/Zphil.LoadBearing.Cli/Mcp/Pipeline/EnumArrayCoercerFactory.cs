using System.Text.Json;
using System.Text.Json.Serialization;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     Coerces malformed enum-array tool inputs into the array the caller clearly intended.
///     Models routinely send a bare string (<c>"Warning"</c>) or a JSON-encoded array of strings
///     (<c>"[\"Warning\",\"Error\"]"</c>) where an enum array is advertised; the SDK default
///     surfaces both as a generic byte-position deserializer error. Enum analog of
///     <see cref="StringArrayCoercerFactory" />, with element validation via
///     <see cref="EnumValidationConverterFactory" />'s "name + <c>Enum.IsDefined</c>" rule.
/// </summary>
/// <remarks>
///     <para>
///         Generic over every <c>TEnum[]</c> parameter, so the enum-array shape is coerced as
///         forgivingly as its scalar (<see cref="EnumValidationConverterFactory" />) and
///         <c>string[]</c> siblings without a per-parameter registration. Handled token shapes for
///         any <c>TEnum[]</c> parameter where <c>TEnum</c> is an enum:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 <c>StartArray</c> → read each element as a string, parse via
///                 <see cref="Enum.TryParse{T}(string,bool,out T)" /> and <see cref="Enum.IsDefined" />.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>String</c> whose contents parse as a JSON array of strings → unwrap and
///                 map each element through the same parse path.
///             </description>
///         </item>
///         <item>
///             <description>
///                 Any other <c>String</c> → single-element <c>[parsed]</c>.
///             </description>
///         </item>
///         <item>
///             <description>
///                 Unknown enum names (at either the top-level string or inside an array) throw
///                 <see cref="UserErrorException" /> with the full valid-values list — same
///                 message shape as <see cref="EnumValidationConverterFactory" />.
///             </description>
///         </item>
///         <item>
///             <description>
///                 Numbers, booleans, objects, or arrays containing non-string elements throw
///                 <see cref="UserErrorException" /> naming the offending token kind. Integer
///                 elements are deliberately NOT admitted as enum values, and neither are
///                 comma-separated name lists — see <see cref="EnumStringHelper.ResolvesByArithmetic" />.
///             </description>
///         </item>
///     </list>
/// </remarks>
internal sealed class EnumArrayCoercerFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsArray && typeToConvert.GetElementType()!.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type elementType = typeToConvert.GetElementType()!;
        Type converterType = typeof(EnumArrayCoercer<>).MakeGenericType(elementType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class EnumArrayCoercer<T> : JsonConverter<T[]> where T : struct, Enum
    {
        public override T[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartArray:
                    return CoercedJsonArray.ReadArray(ref reader, ParseElement, BadElement);

                case JsonTokenType.String:
                    string value = reader.GetString()!;
                    if (CoercedJsonArray.TryParseJsonStringArray(value, ParseElement, out T[] unwrapped)) return unwrapped;

                    return [ParseElement(value)];

                default:
                    throw new UserErrorException(
                        $"Expected a JSON array of {typeof(T).Name}; got {reader.TokenType}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (T element in value) writer.WriteStringValue(element.ToString());

            writer.WriteEndArray();
        }

        // The element projection CoercedJsonArray applies — the shared name rule, so an array element and
        // a scalar parameter admit exactly the same spellings and refuse with exactly the same message.
        private static T ParseElement(string name)
        {
            if (EnumStringHelper.TryParseName(name, out T parsed)) return parsed;

            throw new UserErrorException(EnumStringHelper.InvalidValueMessage<T>(name));
        }

        private static UserErrorException BadElement(JsonTokenType tokenType)
        {
            return new UserErrorException(
                $"Expected a JSON array of {typeof(T).Name}; got element of type {tokenType}.");
        }
    }
}
