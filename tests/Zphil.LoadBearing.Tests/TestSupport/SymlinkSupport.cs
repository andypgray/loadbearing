using Xunit;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Shared helper for the symlink-canonicalization tests. Creates a directory symlink, or skips the
///     calling test (via <see cref="Assert.Skip" />) when the host cannot: Windows without Developer
///     Mode or elevation throws on <see cref="Directory.CreateSymbolicLink" />, so those runners skip
///     while Linux and macOS CI — the platforms whose symlinked roots (macOS <c>/var</c>) motivated the
///     fix — exercise the real symlink path.
/// </summary>
internal static class SymlinkSupport
{
    /// <summary>Creates a directory symlink at <paramref name="linkPath" /> targeting <paramref name="target" />, or skips.</summary>
    public static void CreateDirectorySymlink(string linkPath, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip(
                $"Cannot create a symlink on this host ({ex.GetType().Name}: {ex.Message}); " +
                "needs Developer Mode or elevation on Windows.");
        }
    }
}