using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Zphil.LoadBearing.Cli.Mcp.Pipeline;

/// <summary>
///     The <see cref="JsonSerializerOptions" /> that marshal JSON-RPC tool-call arguments into typed
///     parameters. The registered converter factories replace SDK defaults that would otherwise surface a
///     user-facing input error as an opaque byte-position deserializer message; each documents the token
///     shapes it forgives.
/// </summary>
/// <remarks>
///     An explicit <see cref="DefaultJsonTypeInfoResolver" /> is required on .NET 10 because
///     <c>AIFunctionFactory</c> calls <c>MakeReadOnly()</c> on the options, which throws
///     without a resolver in place.
/// </remarks>
internal static class ToolInputSerializerOptions
{
    public static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters =
        {
            new EnumValidationConverterFactory(),
            new EnumArrayCoercerFactory(),
            new StringArrayCoercerFactory(),
            new StringCoercerFactory(),
            new BoolCoercerFactory()
        }
    };
}
