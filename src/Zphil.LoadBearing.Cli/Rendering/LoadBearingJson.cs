using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The single <see cref="JsonSerializerOptions" /> every CLI JSON renderer (check, status, graph,
///     sarif) shares: camelCase property names, indented, null-omitting, and the relaxed encoder so
///     backticks and em-dashes in rule sentences ride literally — this is CLI output for hooks, not HTML,
///     so they are emitted verbatim rather than as <c>\u</c> escapes. One instance so the four documents
///     cannot drift in escaping or casing; the JSON goldens prove the resulting byte-identity.
/// </summary>
internal static class LoadBearingJson
{
    /// <summary>The shared serializer options for every CLI JSON document.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    ///     The generated metadata for every document root, bound to <see cref="Options" /> once. Declared
    ///     after <see cref="Options" /> because static initializers run in declaration order.
    /// </summary>
    public static readonly LoadBearingJsonContext Context = new(Options);
}

// Source-generated metadata for the four document roots. The generator emits an ordinary property read per
// member, which is what lets the DTOs stay free of implicit-use annotations: nothing about them is
// reflection-only any more. Serialization stays byte-identical — the goldens are the proof.
[JsonSerializable(typeof(CheckJson))]
[JsonSerializable(typeof(StatusJson))]
[JsonSerializable(typeof(GraphJson))]
[JsonSerializable(typeof(SarifLog))]
internal sealed partial class LoadBearingJsonContext : JsonSerializerContext;