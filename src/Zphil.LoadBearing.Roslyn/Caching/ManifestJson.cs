using System.Text.Json;
using System.Text.Json.Serialization;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Replay;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The single <see cref="JsonSerializerOptions" /> both persisted manifests are written and read
///     through — the extraction cache's <see cref="CacheManifest" /> and the build capture's
///     <see cref="CaptureManifest" /> — paired with the source-generated metadata that carries them. One
///     instance so the two files cannot drift in enum spelling or escaping; one context so neither document
///     reaches disk through reflection.
/// </summary>
internal static class ManifestJson
{
    /// <summary>The shared serializer options for both persisted manifests.</summary>
    /// <remarks>
    ///     Compact, with enums written as their names: readability over the few bytes, and rename-safe — an
    ///     unrecognized name degrades to a parse-error miss rather than a silently wrong value. The four
    ///     converters are registered per enum rather than through the open
    ///     <see cref="JsonStringEnumConverter" /> factory, so the set that reaches disk is visible and stays
    ///     trim-safe; neither form carries a naming policy, so both spell the members verbatim and the bytes
    ///     are the ones every cache file written before the generator already holds. A fifth enum joining the
    ///     DTO graph would fall through to the integer default and change the on-disk schema silently, so
    ///     <c>ManifestJsonTests</c> holds this list against the graph rather than leaving it to vigilance.
    ///     Exposed for the round-trip pin.
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter<TypeKind>(),
            new JsonStringEnumConverter<Accessibility>(),
            new JsonStringEnumConverter<MemberKind>(),
            new JsonStringEnumConverter<Lifetime>()
        }
    };

    /// <summary>
    ///     The generated metadata for both manifest roots, bound to <see cref="Options" /> once. Declared
    ///     after <see cref="Options" /> because static initializers run in declaration order.
    /// </summary>
    public static readonly ManifestJsonContext Context = new(Options);
}

// Source-generated metadata for the two manifest roots, plus the bare fragment list the round-trip pin
// serializes on its own. The generator emits an ordinary property read per member, which is what lets the
// manifest DTOs stay free of implicit-use annotations: nothing about them is reflection-only any more.
// Serialization stays byte-identical, and ManifestJsonTests proves that against the reflection resolver
// rather than assuming it.
[JsonSerializable(typeof(CacheManifest))]
[JsonSerializable(typeof(CaptureManifest))]
[JsonSerializable(typeof(IReadOnlyList<CodebaseFragment>))]
internal sealed partial class ManifestJsonContext : JsonSerializerContext;
