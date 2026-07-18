namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     Writes a file atomically: the bytes go to a uniquely-named sibling temp file in the target's own
///     directory, which is then <see cref="File.Move(string,string,bool)" />d over the target. A reader
///     never sees a half-written file, and a crash mid-write leaves either the intact old file or the
///     intact new one — never a truncated one. The unique temp name (not a fixed <c>.tmp</c>) lets two
///     writers of the same target coexist without clobbering each other's scratch file.
/// </summary>
/// <remarks>
///     This is the committed-file sibling of the disposable caches' private atomic writers
///     (<see cref="ExtractionCacheStore" />'s <c>TryWriteAtomic</c>,
///     <see cref="Zphil.LoadBearing.Roslyn.Replay.BinlogCaptureStore" />'s <c>TryWriteManifestAtomic</c>),
///     which now wrap it. The one deliberate difference is failure semantics: this <em>throws</em> rather
///     than swallowing to a bool. Its direct callers write user-owned, version-controlled files — the
///     managed <c>AGENTS.md</c> block and baseline files — where a silently-dropped write would be a lie;
///     a cache write that fails is simply rebuilt next run, so the store wrappers re-swallow the throw
///     themselves. The temp file is deleted on the failure path.
///     Lives in the Roslyn (net10.0) project because the overwriting
///     <see cref="File.Move(string,string,bool)" /> overload does not exist on Core's netstandard2.0;
///     both consumers (Roslyn's baseline store, the CLI's render adapter) reach this project.
/// </remarks>
internal static class AtomicFile
{
    /// <summary>
    ///     Writes <paramref name="bytes" /> to <paramref name="path" /> atomically — a temp sibling then an
    ///     overwriting move — creating the target directory if it is absent. Throws on any I/O failure,
    ///     after a best-effort delete of the temp file.
    /// </summary>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        // Same directory as the target, so File.Move is a rename within one volume (atomic) rather than a
        // cross-volume copy (not); the Guid makes the scratch name unique per write.
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, fullPath, true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup — the temp file is derived scratch, never surfaced to a caller
        }
    }
}