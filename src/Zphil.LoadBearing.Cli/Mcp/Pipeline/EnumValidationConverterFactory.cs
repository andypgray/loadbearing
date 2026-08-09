using System.Text.Json;
using System.Text.Json.Serialization;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     Deserializes enum-typed tool parameters from JSON strings. On an unrecognised name
///     (e.g. <c>severity: "HIGH"</c>) throws a <see cref="UserErrorException" /> that lists
///     every valid value so the model can self-correct the next call.
/// </summary>
/// <remarks>
///     <para>
///         Registered via <see cref="CoercingToolRegistration" /> on the
///         <c>McpServerToolCreateOptions.SerializerOptions</c>
///         used by <c>AIFunctionFactory</c> when marshalling JSON-RPC arguments. The default
///         <see cref="JsonStringEnumConverter" /> raises a generic <c>JsonException</c> that
///         surfaces without the valid-value list, forcing the model to guess.
///     </para>
///     <para>
///         Generic over every <c>T : struct, Enum</c>, so any enum-typed tool parameter is validated
///         this way without a per-parameter registration.
///     </para>
///     <para>
///         One member name, or nothing: integers and comma-separated name lists are both refused, because
///         each lets a caller reach a member by ordinal arithmetic rather than by naming it. See
///         <see cref="EnumStringHelper.ResolvesByArithmetic" />.
///     </para>
/// </remarks>
internal sealed class EnumValidationConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type converterType = typeof(ValidatingJsonStringEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class ValidatingJsonStringEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Our advertised schema is "type": "string"; a client that sends a number is already
            // violating the contract, so reject non-strings with the same valid-values message
            // a bad string would get. This keeps the error surface uniform.
            if (reader.TokenType != JsonTokenType.String)
                throw new UserErrorException(EnumStringHelper.InvalidValueMessage<T>(reader.TokenType.ToString()));

            string? name = reader.GetString();
            if (name is not null && EnumStringHelper.TryParseName(name, out T parsed)) return parsed;

            throw new UserErrorException(EnumStringHelper.InvalidValueMessage<T>(name));
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
