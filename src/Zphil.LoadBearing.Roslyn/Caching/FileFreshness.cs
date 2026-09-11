namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A file's recorded freshness fingerprint: whether it existed, its last-write time and its byte length
///     at the moment of capture, plus the instant of the capture itself. Take one with
///     <see cref="Capture" /> or <see cref="CaptureUnverified" />, then ask <see cref="MatchesStat" /> or
///     <see cref="CanTrust" /> whether a fresh capture of the same path still matches. It knows how to stat
///     a path and how to reason about the window in which a timestamp cannot be trusted; what to do about a
///     mismatch is the caller's business.
/// </summary>
/// <remarks>
///     <para>
///         Filesystems record last-write times coarsely (the FAT/exFAT floor is two seconds, while NTFS is far finer),
///         so a fingerprint captured within <see cref="RacyWindow" /> of the file's own timestamp cannot prove the file
///         is unchanged: a write in the same tick would share that timestamp. Such a fingerprint stays unpromoted
///         (<see cref="IsPromoted" /> is false) until it has been checked against the file's content once and captured
///         again comfortably outside the window. That is the difference between the two comparisons:
///         <see cref="CanTrust" /> insists on it and <see cref="MatchesStat" /> does not.
///     </para>
///     <para>
///         Any difference in timestamp counts as a change, including one that moved backwards: a timestamp-preserving
///         restore is a difference, not proof of freshness. <see cref="Length" /> catches a change that put the
///         recorded timestamp back exactly. One case gets past both: equal timestamp, equal length and different
///         content, on an already-promoted file. Nothing here watches the filesystem, so it takes the caller's next
///         wholesale reload, or its next run, to pick such a change up.
///     </para>
/// </remarks>
public readonly record struct FileFreshness(bool Exists, DateTime LastWriteTimeUtc, long Length, DateTime RecordedAtUtc)
{
    /// <summary>
    ///     The window within which a file's last-write time cannot be trusted on its own: two seconds, which
    ///     covers the FAT/exFAT tick floor and is conservative on NTFS. A fingerprint captured this close to
    ///     the file's own timestamp needs one content check before <see cref="CanTrust" /> will accept it.
    /// </summary>
    public static readonly TimeSpan RacyWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Gets whether this fingerprint was captured more than <see cref="RacyWindow" /> after the file's own
    ///     last-write time, so a later timestamp match can be trusted as proof the file is unchanged.
    /// </summary>
    public bool IsPromoted => Exists && RecordedAtUtc - LastWriteTimeUtc > RacyWindow;

    /// <summary>
    ///     Stats <paramref name="fullPath" /> now and records the capture instant as the current UTC time. A
    ///     file whose last-write time is already older than <see cref="RacyWindow" /> is therefore captured
    ///     promoted, and can be trusted on a later timestamp match with no content read. A missing file yields
    ///     a fingerprint whose <see cref="Exists" /> is false, which is a mismatch against one that exists — so
    ///     a file that later appears reads as a change.
    /// </summary>
    public static FileFreshness Capture(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return info.Exists
            ? new FileFreshness(true, info.LastWriteTimeUtc, info.Length, DateTime.UtcNow)
            : new FileFreshness(false, default, 0, DateTime.UtcNow);
    }

    /// <summary>
    ///     Stats <paramref name="fullPath" /> now but records the capture instant as the file's own last-write
    ///     time, which leaves the fingerprint unpromoted until its first content check. Use it when recording a
    ///     file you have just read: "unchanged since the read" cannot be told from "rewritten in the same tick
    ///     with its timestamp preserved" without reading the content once, and this forces that one read. A
    ///     missing file yields a fingerprint whose <see cref="Exists" /> is false.
    /// </summary>
    public static FileFreshness CaptureUnverified(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return info.Exists
            ? new FileFreshness(true, info.LastWriteTimeUtc, info.Length, info.LastWriteTimeUtc)
            : new FileFreshness(false, default, 0, default);
    }

    /// <summary>
    ///     Returns whether <paramref name="current" />, a fresh capture of the same path, has the same
    ///     existence, the same last-write time and the same length. A stat comparison only: no content is read
    ///     and the racy window does not apply, which suits files cheap enough to reload wholesale on any
    ///     difference. An existence flip (a file that was absent and has appeared) is a mismatch.
    /// </summary>
    public bool MatchesStat(FileFreshness current)
    {
        return Exists == current.Exists
               && LastWriteTimeUtc == current.LastWriteTimeUtc
               && Length == current.Length;
    }

    /// <summary>
    ///     Returns whether this fingerprint proves the file is unchanged against <paramref name="current" />, a
    ///     fresh capture of the same path, without reading its content: both exist, last-write time and length
    ///     still match, and this fingerprint is past the racy window (<see cref="IsPromoted" />). Any doubt
    ///     returns false, so the caller reads the file and compares the content itself.
    /// </summary>
    public bool CanTrust(FileFreshness current)
    {
        return Exists
               && current.Exists
               && LastWriteTimeUtc == current.LastWriteTimeUtc
               && Length == current.Length
               && IsPromoted;
    }
}
