using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     Maps a pipeline exception to stderr text and an exit code (always 2 — "everything else") for
///     every command (check, explain, render). <see cref="UserErrorException" /> renders message-only;
///     <see cref="SpecValidationException" /> lists every validation error at once (agents fix a spec
///     in one pass); anything else is a bug and gets a full stack trace. Pure and Roslyn-free, so it
///     unit-tests without a workspace. The user-facing text is factored into
///     <see cref="UserFacingMessage" /> so the MCP <c>GlobalCallToolFilter</c> renders the identical
///     multi-line body on its surface.
/// </summary>
internal static class CliErrorMapper
{
    /// <summary>
    ///     The multi-line user-facing message for an expected error, or null when the exception is an
    ///     unexpected bug (which the CLI dumps as a stack trace and the MCP filter logs as a warning).
    ///     Both surfaces render this identical text — a <see cref="UserErrorException" />'s message
    ///     verbatim, or a <see cref="SpecValidationException" />'s "Spec validation failed:" header
    ///     followed by one indented line per error.
    /// </summary>
    public static string? UserFacingMessage(Exception exception)
    {
        return exception switch
        {
            UserErrorException => exception.Message,
            SpecValidationException validation =>
                "Spec validation failed:\n" + string.Join("\n", validation.Errors.Select(e => $"  - {e.Message}")),
            _ => null
        };
    }

    public static int Write(Exception exception, TextWriter error)
    {
        string? userFacing = UserFacingMessage(exception);
        if (userFacing is null)
        {
            error.WriteLine(exception.ToString());
            return 2;
        }

        // WriteLine per LF-split line so a multi-line message adopts the writer's newline, byte-identical
        // to the former per-line emission (and to the MCP surface after normalization).
        foreach (string line in userFacing.Split('\n')) error.WriteLine(line);
        return 2;
    }
}