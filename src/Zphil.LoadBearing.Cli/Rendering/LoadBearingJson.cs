using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The single <see cref="JsonSerializerOptions" /> every CLI JSON renderer (check, status, graph,
///     sarif, hook) shares: camelCase property names, camelCase enum values, indented, null-omitting, and
///     the relaxed encoder so backticks and em-dashes in rule sentences ride literally — this is CLI output
///     for hooks, not HTML, so they are emitted verbatim rather than as <c>\u</c> escapes. One instance so
///     the five documents cannot drift in escaping or casing; the JSON goldens prove the resulting
///     byte-identity.
/// </summary>
internal static class LoadBearingJson
{
    /// <summary>The shared serializer options for every CLI JSON document.</summary>
    /// <remarks>
    ///     The four enum converters are the value half of the casing contract this class exists to own.
    ///     The wire slots are typed as the enums they are, so the casing is applied here once instead of
    ///     by a <c>Camel</c> helper per renderer, and a DTO can no longer be handed a hand-built string or
    ///     the wrong enum stringified. Registered per enum rather than through the open
    ///     <see cref="JsonStringEnumConverter" /> factory so the set that reaches the wire is visible and
    ///     stays trim-safe; the source-generated context binds runtime converters, which is what lets
    ///     these apply without the generator being told about them.
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter<Posture>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<RuleStatus>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<ViolationKind>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<CheckWarningKind>(JsonNamingPolicy.CamelCase)
        }
    };

    /// <summary>
    ///     The generated metadata for every document root, bound to <see cref="Options" /> once. Declared
    ///     after <see cref="Options" /> because static initializers run in declaration order.
    /// </summary>
    public static readonly LoadBearingJsonContext Context = new(Options);

    /// <summary>
    ///     A count for an additive wire slot: itself when there is something to report, and null — omitted,
    ///     under <see cref="Options" />' null handling — at zero. Zero and absent say the same thing, and a
    ///     key that appears only when it means something keeps a clean document clean.
    /// </summary>
    public static int? OmitZero(int count)
    {
        return count > 0 ? count : null;
    }

    /// <summary>
    ///     A flag for an additive wire slot: <see langword="true" /> when there is something to report, and
    ///     null — omitted — when there is not. <see cref="OmitZero" />'s reasoning for a boolean fact: false
    ///     and absent say the same thing, so a flag that appears only when it means something keeps a clean
    ///     document clean.
    /// </summary>
    public static bool? OmitFalse(bool flag)
    {
        return flag ? true : null;
    }
}

// Source-generated metadata for the five document roots. The generator emits an ordinary property read per
// member, which is what lets the DTOs stay free of implicit-use annotations: nothing about them is
// reflection-only. Serialization stays byte-identical — the goldens are the proof.
[JsonSerializable(typeof(CheckJson))]
[JsonSerializable(typeof(StatusJson))]
[JsonSerializable(typeof(GraphJson))]
[JsonSerializable(typeof(SarifLog))]
[JsonSerializable(typeof(HookJson))]
internal sealed partial class LoadBearingJsonContext : JsonSerializerContext;
