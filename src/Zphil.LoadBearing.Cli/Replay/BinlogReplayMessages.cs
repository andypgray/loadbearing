namespace Zphil.LoadBearing.Cli.Replay;

/// <summary>
///     The CLI-side user-facing text for the <c>--binlog</c> replay path: the loud refusals for
///     a user-supplied binlog that cannot be replayed, and the soft notice for a persisted capture whose copy
///     fails to replay at runtime. The sibling of <see cref="Roslyn.Replay.BinlogCaptureStore" />'s own
///     factories (which own the ingest refusals and the structural-invalidation notices); these three own the
///     replay-failure surface the gate raises. Exposed as factories so tests pin the text without duplicating
///     the format, and so the first-line extraction lives in exactly one place.
/// </summary>
internal static class BinlogReplayMessages
{
    /// <summary>The refusal when the explicit <c>--binlog</c> path does not exist (exit 2); <c>{0}</c> is the as-typed value.</summary>
    internal static string MissingFileMessage(string binlogArgument)
    {
        return $"--binlog '{binlogArgument}' was not found. Pass a .binlog produced by building this "
               + "solution (e.g. dotnet build -bl).";
    }

    /// <summary>
    ///     The two-line refusal when the explicit <c>--binlog</c> exists but cannot be replayed — corrupt, not
    ///     a binlog, or holding no C# calls (exit 2). <c>{0}</c> is the as-typed value; the first line of
    ///     <paramref name="underlyingMessage" /> is quoted so the cause is visible without a stack trace.
    /// </summary>
    internal static string ReplayFailedMessage(string binlogArgument, string underlyingMessage)
    {
        return $"--binlog '{binlogArgument}' could not be replayed: {FirstLine(underlyingMessage)}\n"
               + "Rebuild with -bl and pass the fresh binlog.";
    }

    /// <summary>
    ///     The soft notice (printed behind a <c>warning: </c> prefix, then a cold fallback) when a
    ///     structurally-valid capture's binlog copy fails to replay at runtime — a torn copy or a vanished
    ///     reference. <c>{0}</c> is the first line of the underlying failure message.
    /// </summary>
    internal static string CaptureReplayFailedNotice(string underlyingMessage)
    {
        return $"build capture could not be replayed ({FirstLine(underlyingMessage)}); running a design-time "
               + "build instead. Re-capture: rebuild with -bl and re-run with --binlog.";
    }

    // The first physical line of a possibly-multi-line exception message — the underlying reader errors carry
    // stack-shaped detail on later lines that would bury the refusal.
    private static string FirstLine(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;

        int end = message.IndexOfAny(['\r', '\n']);
        return end < 0 ? message : message[..end];
    }
}