namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     Writes or copies a file atomically <em>and</em> durably: the bytes go to a uniquely-named sibling temp
///     file in the target's own directory, are flushed to disk, and the temp is then
///     <see cref="File.Move(string,string,bool)" />d over the target. A reader never sees a half-written file;
///     a crash mid-write leaves either the intact old file or the intact new one — never a truncated one; and,
///     because the flush persists the bytes before the rename, a power loss in the move window cannot leave the
///     renamed target pointing at content still sitting unwritten in the OS cache. The unique temp name (not a
///     fixed <c>.tmp</c>) lets two writers of the same target coexist without clobbering each other's scratch
///     file.
/// </summary>
/// <remarks>
///     This is the committed-file sibling of the disposable caches' private atomic writers
///     (<see cref="ExtractionCacheStore" />'s <c>TryWriteAtomic</c>,
///     <see cref="Zphil.LoadBearing.Roslyn.Replay.BinlogCaptureStore" />'s <c>TryWriteManifestAtomic</c>),
///     which now wrap it, and it also owns that store's binlog copy through <see cref="Copy" />. The one
///     deliberate difference is failure semantics: this <em>throws</em> rather than swallowing to a bool. Its
///     direct callers write user-owned, version-controlled files — the managed <c>AGENTS.md</c> block and
///     baseline files — where a silently-dropped write would be a lie; a cache write that fails is simply
///     rebuilt next run, so the store wrappers re-swallow the throw themselves. The temp file is deleted on
///     the failure path.
///     Lives in the Roslyn (net10.0) project because the overwriting
///     <see cref="File.Move(string,string,bool)" /> overload does not exist on Core's netstandard2.0;
///     both consumers (Roslyn's baseline store, the CLI's render adapter) reach this project.
/// </remarks>
internal static class AtomicFile
{
    /// <summary>
    ///     Writes <paramref name="bytes" /> to <paramref name="path" /> atomically and durably — a temp
    ///     sibling, flushed to disk, then an overwriting move — creating the target directory if it is absent.
    ///     Throws on any I/O failure, after a best-effort delete of the temp file.
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
            WriteDurably(tempPath, bytes);
            File.Move(tempPath, fullPath, true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    ///     Copies <paramref name="source" /> to <paramref name="destination" /> atomically and durably — into a
    ///     temp sibling, flushed to disk, then an overwriting move — creating the destination directory if it is
    ///     absent. Throws on any I/O failure, after a best-effort delete of the temp file.
    /// </summary>
    public static void Copy(string source, string destination)
    {
        string fullDestination = Path.GetFullPath(destination);
        string directory = Path.GetDirectoryName(fullDestination)!;
        Directory.CreateDirectory(directory);

        // Same directory as the destination, so the promoting File.Move is an atomic same-volume rename; the
        // Guid makes the scratch name unique per copy.
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(fullDestination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(source, tempPath, true);
            FlushToDisk(tempPath);
            File.Move(tempPath, fullDestination, true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    // Streams the bytes to disk and fsyncs them before returning, so the caller's subsequent File.Move promotes
    // content already persisted — not merely handed to the OS cache, which a power loss could still drop after
    // the atomic-but-not-durable File.WriteAllBytes this replaces. The using declaration closes the stream at
    // method exit, before the caller renames — the file must be closed for File.Move to succeed on Windows.
    private static void WriteDurably(string path, byte[] bytes)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    // Re-opens an already-written file purely to fsync its bytes (the copy path: File.Copy leaves them in the
    // OS cache). Flush(flushToDisk: true) issues the platform disk sync even with no buffered writes of its own.
    private static void FlushToDisk(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Flush(true);
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