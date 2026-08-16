namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     A throwaway extraction-cache root for a suite that points runs at one through
///     <c>LOADBEARING_CACHE_DIR</c>: a private path under the given <see cref="TestTempRoot" /> category,
///     deleted when the <c>using</c> ends.
/// </summary>
/// <remarks>
///     The category is the caller's, so two suites driving cache runs at once never share a root — and each
///     one's roots are swept under its own name.
/// </remarks>
internal sealed class TempCacheRoot : IDisposable
{
    private readonly TempDirectory temp;

    /// <param name="category">The <see cref="TestTempRoot" /> category the root is minted under.</param>
    public TempCacheRoot(string category)
    {
        temp = TestTempRoot.Fresh(category);
        Root = temp.UniqueChildPath();
    }

    // Not created: the cache root is the store's to mint, and every run here starts from its absence.
    public string Root { get; }

    public void Dispose()
    {
        temp.Dispose();
    }

    /// <summary>Whether any <c>cache.json</c> has been written anywhere under the root.</summary>
    public bool HasCacheFile()
    {
        return Directory.Exists(Root)
               && Directory.EnumerateFiles(Root, "cache.json", SearchOption.AllDirectories)
                   .Any();
    }
}
