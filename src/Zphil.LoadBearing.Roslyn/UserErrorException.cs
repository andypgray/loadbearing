namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     An error whose message is meant to be shown to the person who ran the tool, complete on its own: a
///     missing or ambiguous solution, an unreadable solution filter, a spec that cannot be resolved or was
///     never built, a spec assembly declaring no spec class, a baseline file that has been tampered with.
///     Throw it when the fault is something the reader can fix; anything else is a bug and should surface
///     as itself.
/// </summary>
/// <remarks>
///     Both of LoadBearing's own hosts treat it that way: the CLI prints the message alone to stderr and exits 2, and
///     the MCP server returns it to the client as an error result. Any other exception is treated as a bug instead: the
///     CLI writes its whole stack trace, and the MCP server logs it once as a warning.
/// </remarks>
public sealed class UserErrorException : InvalidOperationException
{
    /// <summary>Initializes a new instance with the message rendered to the user.</summary>
    /// <param name="message">The user-facing text, complete on its own — no stack trace accompanies it.</param>
    public UserErrorException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance with the message rendered to the user and the fault beneath it.</summary>
    /// <param name="message">The user-facing text, complete on its own — no stack trace accompanies it.</param>
    /// <param name="innerException">The underlying fault this error was raised for.</param>
    public UserErrorException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
