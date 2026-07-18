using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The single <see cref="JsonSerializerOptions" /> every CLI JSON renderer (check, status, graph)
///     shares: camelCase property names, indented, null-omitting, and the relaxed encoder so backticks and
///     em-dashes in rule sentences ride literally — this is CLI output for hooks, not HTML, so they are
///     emitted verbatim rather than as <c>\u</c> escapes. One instance so the three documents cannot drift
///     in escaping or casing; the JSON goldens prove the resulting byte-identity.
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
}