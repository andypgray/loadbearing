using System.Security.Cryptography;
using System.Text;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     Derives the on-disk location of a solution's persisted extraction cache. The default root is
///     <c>%LOCALAPPDATA%/Zphil.LoadBearing/cache</c> (the same <c>%LOCALAPPDATA%/Zphil.LoadBearing</c> base
///     the Serilog file log uses), and each solution gets its own subdirectory named
///     <c>&lt;sln-name&gt;-&lt;sha256-prefix-of-canonical-path&gt;</c>. That directory holds the Phase 11
///     fragment cache (<c>cache.json</c>) and, from Phase 12, the binlog build-capture pair alongside it: a
///     <c>capture.json</c> manifest and the <c>capture.binlog</c> copy the replay path reuses (see
///     <see cref="CaptureManifestPath" />/<see cref="CaptureBinlogPath" />). All three are independent,
///     disposable local derived data keyed to the one solution.
/// </summary>
/// <remarks>
///     <para>
///         The root is overridable via a parameter (the CLI passes the <c>LOADBEARING_CACHE_DIR</c> value
///         through <see cref="System.Environment" />'s seam in a later work package, and tests point it at a
///         throwaway temp directory) — this type never reads an environment variable itself, keeping the
///         Roslyn project off the <c>mcp/env-through-seam</c> ratchet.
///     </para>
///     <para>
///         The path identity reuses <see cref="PathCanonicalizer" /> (symlink-resolved, fully-qualified) so
///         the same solution opened through a symlinked root still maps to one cache directory, matching the
///         canonicalization the Freeze tripwire and spec resolution already use. On a case-insensitive file
///         system the canonical path is lowercased before hashing so case-variant spellings unify too.
///     </para>
/// </remarks>
internal static class CacheLocations
{
    private const string ProductFolder = "Zphil.LoadBearing";
    private const string CacheFolder = "cache";
    private const string CacheFileName = "cache.json";
    private const string CaptureManifestFileName = "capture.json";
    private const string CaptureBinlogFileName = "capture.binlog";

    // 16 hex chars (64 bits) of the canonical-path SHA-256 — ample to keep two distinct solutions from
    // colliding while keeping the directory name short. The <sln-name> prefix is for human readability only.
    private const int PathHashHexLength = 16;

    /// <summary>The default cache root — <c>%LOCALAPPDATA%/Zphil.LoadBearing/cache</c>.</summary>
    internal static string DefaultCacheRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolder,
            CacheFolder);
    }

    /// <summary>
    ///     The cache directory for <paramref name="solutionPath" /> under
    ///     <paramref name="cacheRootOverride" /> (or the default root when null/blank).
    /// </summary>
    internal static string CacheDirectory(string solutionPath, string? cacheRootOverride)
    {
        string root = string.IsNullOrWhiteSpace(cacheRootOverride) ? DefaultCacheRoot() : cacheRootOverride;
        string canonical = PathCanonicalizer.Resolve(solutionPath);
        string identityKey = PathComparison.Comparison == StringComparison.OrdinalIgnoreCase
            ? canonical.ToLowerInvariant()
            : canonical;

        string hashHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityKey))).ToLowerInvariant();
        string name = Path.GetFileNameWithoutExtension(canonical);
        return Path.Combine(root, $"{name}-{hashHex[..PathHashHexLength]}");
    }

    /// <summary>The absolute path to the single atomic cache file for <paramref name="solutionPath" />.</summary>
    internal static string CacheFilePath(string solutionPath, string? cacheRootOverride)
    {
        return Path.Combine(CacheDirectory(solutionPath, cacheRootOverride), CacheFileName);
    }

    /// <summary>
    ///     The absolute path to the Phase 12 build-capture manifest (<c>capture.json</c>) for
    ///     <paramref name="solutionPath" /> — the structure-only-keyed sidecar beside
    ///     <see cref="CacheFilePath" /> in the same per-solution directory.
    /// </summary>
    internal static string CaptureManifestPath(string solutionPath, string? cacheRootOverride)
    {
        return Path.Combine(CacheDirectory(solutionPath, cacheRootOverride), CaptureManifestFileName);
    }

    /// <summary>
    ///     The absolute path to the captured binlog copy (<c>capture.binlog</c>) for
    ///     <paramref name="solutionPath" /> — the build log the replay path re-reads while the capture stays
    ///     structurally valid, stored beside its <see cref="CaptureManifestPath" /> manifest.
    /// </summary>
    internal static string CaptureBinlogPath(string solutionPath, string? cacheRootOverride)
    {
        return Path.Combine(CacheDirectory(solutionPath, cacheRootOverride), CaptureBinlogFileName);
    }
}