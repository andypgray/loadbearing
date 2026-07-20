using System.Reflection;
using System.Security.Cryptography;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The pure file-stamping primitives shared by the twin structure-keyed cache stores — the Phase 11
///     <see cref="ExtractionCacheStore" /> and the Phase 12
///     <see cref="Zphil.LoadBearing.Roslyn.Replay.BinlogCaptureStore" />. Both persist a
///     <see cref="FileStamp" /> per structural input and validate it against disk the same way: stat first,
///     and re-hash only when the stat moved (a bare mtime touch on unchanged bytes must not invalidate).
///     Centralizing the byte-identical copies keeps that racy-window discipline defined once, so the two
///     stores cannot drift apart.
/// </summary>
/// <remarks>
///     Everything here is pure over its arguments and disk state. Each store keeps its own manifest-typed
///     read/write wrappers, its own promotion policy, and its own <c>ContentHashCount</c> observable — the
///     last threaded back through <see cref="CheckStructural" />'s <c>onHash</c> callback so a store still
///     counts exactly its own validation reads without this shared code knowing the counter exists.
/// </remarks>
internal static class FileStamping
{
    /// <summary>
    ///     Probe files whose presence anywhere from a project directory up to the solution directory changes
    ///     the build — stamped even when absent, so a newly-appearing one is an existence flip. Shared by both
    ///     cache stores and the warm <see cref="WorkspaceSession" /> reconcile sweep.
    /// </summary>
    internal static readonly string[] StructuralProbeFileNames =
        ["Directory.Build.props", "Directory.Build.targets", "global.json"];

    /// <summary>
    ///     The tool version stamped into a manifest: this (Roslyn) assembly's informational version
    ///     (e.g. <c>0.1.0+&lt;sha&gt;</c>). The .NET SDK appends the commit hash, giving per-commit cache
    ///     invalidation during development. All packages ship lockstep, so the value equals the tool's.
    /// </summary>
    internal static readonly string CurrentToolVersion =
        typeof(FileStamping).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>
    ///     Stamps <paramref name="path" /> as it is on disk now — existence, mtime/length, content hash, and the
    ///     racy-window promotion verdict.
    /// </summary>
    internal static FileStamp StampOf(string path)
    {
        string full = Path.GetFullPath(path);
        FileFreshness fresh = FileFreshness.Capture(full);
        if (!fresh.Exists) return new FileStamp(full, false, 0, 0, null, false);

        return new FileStamp(full, true, fresh.LastWriteTimeUtc.Ticks, fresh.Length, TryHashFile(full), fresh.IsPromoted);
    }

    /// <summary>
    ///     Rebuilds a stamp from a fresh <see cref="FileFreshness" /> capture and an already-computed hash — a promotion
    ///     after a bare touch.
    /// </summary>
    internal static FileStamp RefreshStamp(string path, FileFreshness current, string? sha)
    {
        return new FileStamp(path, current.Exists, current.LastWriteTimeUtc.Ticks, current.Length, sha, current.IsPromoted);
    }

    /// <summary>
    ///     Lowercase-hex SHA-256 of the file's bytes, or null when it is unreadable — an I/O failure degrades, never
    ///     throws.
    /// </summary>
    internal static string? TryHashFile(string path)
    {
        try
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Reconstitutes a <see cref="FileFreshness" /> from a stamp for a pure stat comparison; the racy verdict is the
    ///     persisted <see cref="FileStamp.Promoted" /> flag, not a re-derived instant.
    /// </summary>
    internal static FileFreshness ToFreshness(FileStamp stamp)
    {
        return new FileFreshness(stamp.Exists, new DateTime(stamp.LastWriteTimeUtcTicks, DateTimeKind.Utc), stamp.Length, default);
    }

    /// <summary>
    ///     Scalar value-equality of two stamp lists (<see cref="FileStamp" /> is a record) — whether a validation
    ///     refreshed any stamp.
    /// </summary>
    internal static bool StampsEqual(IReadOnlyList<FileStamp> left, IReadOnlyList<FileStamp> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
            if (left[i] != right[i])
                return false; // FileStamp is a record ⇒ scalar value equality
        return true;
    }

    /// <summary>The absolute path of a project's restore assets file (<c>obj/project.assets.json</c>) — a structural input.</summary>
    internal static string AssetsPathOf(string projectDirectory)
    {
        return Path.GetFullPath(Path.Combine(projectDirectory, "obj", "project.assets.json"));
    }

    /// <summary>
    ///     Validates one structural stamp against disk: an existence flip is a change; a promoted stat match is
    ///     trusted without a read; otherwise the file is re-hashed (invoking <paramref name="onHash" /> first,
    ///     so the caller can count the read) — a real content change is a change, a bare touch refreshes the
    ///     stamp so the next sweep is pure-stat.
    /// </summary>
    /// <param name="stamp">The recorded stamp to check against current disk state.</param>
    /// <param name="onHash">Invoked once immediately before each content hash, so a store can maintain its own read counter.</param>
    /// <returns>
    ///     <c>Changed</c> is true on an existence flip or real content change; <c>Refreshed</c> is the stamp to carry
    ///     forward.
    /// </returns>
    internal static (bool Changed, FileStamp Refreshed) CheckStructural(FileStamp stamp, Action? onHash = null)
    {
        FileFreshness current = FileFreshness.Capture(stamp.Path);
        if (stamp.Exists != current.Exists) return (true, stamp);
        if (!stamp.Exists) return (false, stamp);
        if (ToFreshness(stamp).MatchesStat(current) && stamp.Promoted) return (false, stamp);

        onHash?.Invoke();
        string? sha = TryHashFile(stamp.Path);
        if (sha is null || !string.Equals(sha, stamp.Sha256, StringComparison.Ordinal)) return (true, stamp);

        // Content unchanged despite the stat delta (a bare touch): refresh so the next sweep is pure-stat.
        return (false, RefreshStamp(stamp.Path, current, sha));
    }
}