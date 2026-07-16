namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     A file's recorded freshness fingerprint: its existence, last-write time, and byte length at the
///     moment we captured it, plus the wall-clock instant of the capture. This is the shared primitive
///     behind two staleness checks — the warm <see cref="WorkspaceSession" />'s per-call reconcile sweep
///     and (from Phase 11 WP6) the persisted extraction cache's validation pass — so it carries no
///     session or cache coupling; it only knows how to stat a path and how to reason about the racy
///     window.
/// </summary>
/// <remarks>
///     <para>
///         <b>The racy window.</b> Filesystems record mtimes at coarse resolution (the FAT/exFAT floor
///         is 2 seconds; NTFS is far finer at ~100 ns). If we captured a fingerprint within
///         <see cref="RacyWindow" /> of the file's own mtime, a same-tick external write could share that
///         mtime, so mtime-equality alone cannot prove the file is unchanged. Such a fingerprint stays
///         <em>unpromoted</em> (<see cref="IsPromoted" /> is false) until it has been content-verified
///         once and re-captured comfortably outside the window — see <see cref="CanTrust" />.
///     </para>
///     <para>
///         <b>Any mtime difference is a change.</b> A backwards mtime (a timestamp-preserving restore:
///         <c>robocopy /COPY:T</c>, some git tooling, archive extraction) is a difference, not proof of
///         freshness. The <see cref="Length" /> field additionally catches a content change that restores
///         the exact recorded mtime. The one residual blind spot is equal mtime <em>and</em> equal length
///         <em>and</em> changed content on an already-promoted file; without a filesystem watcher (which
///         this design deliberately omits), the next structural change or process restart recovers it.
///     </para>
/// </remarks>
public readonly record struct FileFreshness(bool Exists, DateTime LastWriteTimeUtc, long Length, DateTime RecordedAtUtc)
{
    /// <summary>
    ///     Filesystem-mtime resolution slop. A fingerprint captured within this window of the file's own
    ///     mtime cannot be trusted on mtime-equality alone. 2 seconds covers the FAT/exFAT tick floor and
    ///     is conservative on NTFS.
    /// </summary>
    public static readonly TimeSpan RacyWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     True when this fingerprint was captured comfortably outside the <see cref="RacyWindow" /> — the
    ///     capture instant is more than <see cref="RacyWindow" /> after the file's mtime — so mtime
    ///     equality can be trusted as proof the file is unchanged.
    /// </summary>
    public bool IsPromoted => Exists && RecordedAtUtc - LastWriteTimeUtc > RacyWindow;

    /// <summary>
    ///     Stats <paramref name="fullPath" /> now and records the capture instant as <c>UtcNow</c>. A file
    ///     whose mtime is already older than <see cref="RacyWindow" /> is therefore captured
    ///     <see cref="IsPromoted">promoted</see> — trustworthy on a later mtime-equality check. A missing
    ///     file yields <see cref="Exists" /> <c>= false</c>.
    /// </summary>
    public static FileFreshness Capture(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return info.Exists
            ? new FileFreshness(true, info.LastWriteTimeUtc, info.Length, DateTime.UtcNow)
            : new FileFreshness(false, default, 0, DateTime.UtcNow);
    }

    /// <summary>
    ///     Stats <paramref name="fullPath" /> now but records the capture instant as the file's own mtime,
    ///     giving a zero racy gap so the fingerprint stays <see cref="IsPromoted">unpromoted</see> until
    ///     its first content verification. Used when recording a file we have just loaded: "unchanged
    ///     since load" cannot be told from "rewritten in the load tick with a preserved mtime" without one
    ///     verification, so the conservative choice is to force that one check.
    /// </summary>
    public static FileFreshness CaptureUnverified(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return info.Exists
            ? new FileFreshness(true, info.LastWriteTimeUtc, info.Length, info.LastWriteTimeUtc)
            : new FileFreshness(false, default, 0, default);
    }

    /// <summary>
    ///     Pure stat equality against a fresh capture of the same path: same existence, same mtime, same
    ///     length. The structural-file check (sln/csproj/props/assets) uses this — those files are cheap
    ///     to reload wholesale on any delta, so they skip the racy-window content dance entirely. An
    ///     existence flip (an absent probe file that appears) is a mismatch.
    /// </summary>
    public bool MatchesStat(FileFreshness current)
    {
        return Exists == current.Exists
               && LastWriteTimeUtc == current.LastWriteTimeUtc
               && Length == current.Length;
    }

    /// <summary>
    ///     True when this recorded fingerprint proves the file is unchanged against <paramref name="current" />
    ///     (a fresh capture of the same path) <em>without</em> reading its content: both exist, mtime and
    ///     length still match, and this fingerprint is <see cref="IsPromoted">promoted</see> past the racy
    ///     window. Any doubt returns false, so the caller re-reads and content-verifies.
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