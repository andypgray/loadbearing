namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Captures a file's bytes and mtime so a test that mutates a committed fixture in place can put it back
///     exactly as it found it — the mtime included, because freshness checks read it.
/// </summary>
internal static class FileSnapshot
{
    /// <summary>The file's current bytes and last-write time.</summary>
    internal static (byte[] bytes, DateTime mtime) Capture(string path)
    {
        return (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path));
    }

    /// <summary>Writes <paramref name="snapshot" /> back over the file, restoring its last-write time too.</summary>
    internal static void Restore(string path, (byte[] bytes, DateTime mtime) snapshot)
    {
        File.WriteAllBytes(path, snapshot.bytes);
        File.SetLastWriteTimeUtc(path, snapshot.mtime);
    }
}
