using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Replay;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     How both persisted manifests reach disk and come back — the extraction cache's
///     <see cref="CacheManifest" /> and the build capture's <see cref="CaptureManifest" />: one
///     <see cref="JsonSerializerOptions" />, the source-generated metadata that carries them, and the
///     <see cref="TryRead{T}">read</see>/<see cref="TryWriteAtomic{T}">write</see> pair they share. One
///     instance so the two files cannot drift in enum spelling or escaping; one context so neither document
///     reaches disk through reflection; one read/write pair so neither store can quietly degrade differently
///     from the other.
/// </summary>
internal static class ManifestJson
{
    /// <summary>The shared serializer options for both persisted manifests.</summary>
    /// <remarks>
    ///     Compact, with enums written as their names: readability over the few bytes, and rename-safe — an
    ///     unrecognized name degrades to a parse-error miss rather than a silently wrong value. The
    ///     converters are registered per enum rather than through the open
    ///     <see cref="JsonStringEnumConverter" /> factory, so the set that reaches disk is visible and stays
    ///     trim-safe; neither form carries a naming policy, so both spell the members verbatim and the bytes
    ///     are the ones every cache file written before the generator already holds. An enum joining the DTO
    ///     graph with no entry here would fall through to the integer default and change the on-disk schema
    ///     silently, so <c>ManifestJsonTests</c> holds this list against the graph rather than leaving it to
    ///     vigilance — and that test is what says a new arrival is registered here rather than by a
    ///     <see cref="JsonConverterAttribute" /> of its own, which would satisfy the serializer while
    ///     emptying the list of the property it is kept for. Exposed for the round-trip pin.
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter<TypeKind>(),
            new JsonStringEnumConverter<Accessibility>(),
            new JsonStringEnumConverter<MemberKind>(),
            new JsonStringEnumConverter<Lifetime>(),
            new JsonStringEnumConverter<UnsupportedProjectKind>()
        }
    };

    /// <summary>
    ///     The generated metadata for both manifest roots, bound to <see cref="Options" /> once. Declared
    ///     after <see cref="Options" /> because static initializers run in declaration order.
    /// </summary>
    public static readonly ManifestJsonContext Context = new(Options);

    /// <summary>
    ///     Reads <paramref name="path" /> back as a <typeparamref name="T" />, or null when it is absent,
    ///     torn, garbled, or unreadable.
    /// </summary>
    /// <remarks>
    ///     <b>The caught set is the degradation contract</b> both stores' remarks promise, which is why it is
    ///     stated once here rather than twice in near-identical copies: these files are disposable derived
    ///     data with no tamper story, so anything unreadable must degrade to "rebuild it" and never to a loud
    ///     error or — far worse — a wrong answer. Absence needs no separate probe: a missing file or
    ///     directory arrives as an <see cref="IOException" /> and answers null down the same path. A caller
    ///     spells <c>File.Exists</c> itself only when it must tell "nothing cached yet" apart from "something
    ///     cached that no longer holds", because those two are different things to say to an operator.
    /// </remarks>
    internal static T? TryRead<T>(string path, JsonTypeInfo<T> typeInfo) where T : class
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize(bytes, typeInfo);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Serializes <paramref name="value" /> and puts it at <paramref name="path" /> atomically, reporting
    ///     whether it landed.
    /// </summary>
    /// <remarks>
    ///     Best-effort by the same contract <see cref="TryRead{T}" /> reads under: the caller already holds
    ///     the answer it was going to give, so a write it cannot complete costs the next run a rebuild and
    ///     nothing more — never the current run. <see cref="AtomicFile" /> is what stops a reader ever seeing
    ///     half a document; this method is what stops a failed write ever surfacing as an error.
    /// </remarks>
    internal static bool TryWriteAtomic<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
            AtomicFile.WriteAllBytes(path, bytes);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
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
