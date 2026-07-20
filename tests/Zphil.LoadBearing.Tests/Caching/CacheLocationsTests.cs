using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Tests.Caching;

/// <summary>
///     <see cref="CacheLocations" /> path derivation: the default cache root is
///     <c>%LOCALAPPDATA%/Zphil.LoadBearing/cache</c>, and a null override falls back to it — so a derived
///     cache-file path for any solution roots there and is named <c>cache.json</c>. The type reads no
///     environment variable itself (the override parameter is the only seam), keeping the Roslyn project off
///     the <c>mcp/env-through-seam</c> ratchet.
/// </summary>
public sealed class CacheLocationsTests
{
    [Fact]
    public void DefaultCacheRoot_IsLocalAppDataUnderProductCacheFolder()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Zphil.LoadBearing",
            "cache");

        CacheLocations.DefaultCacheRoot().ShouldBe(expected);
    }

    [Fact]
    public void CacheFilePath_NullOverride_RootsUnderDefaultCacheRoot()
    {
        // A null/blank override falls back to DefaultCacheRoot; the per-solution cache file then lives beneath
        // it (in a <sln-name>-<hash> subdirectory), named cache.json. Using a nonexistent temp path is safe —
        // PathCanonicalizer.Resolve falls back to GetFullPath when the leaf does not exist.
        string solutionPath = Path.Combine(Path.GetTempPath(), "Some.Solution.sln");

        string path = CacheLocations.CacheFilePath(solutionPath, null);

        path.ShouldStartWith(CacheLocations.DefaultCacheRoot());
        Path.GetFileName(path).ShouldBe("cache.json");
    }
}