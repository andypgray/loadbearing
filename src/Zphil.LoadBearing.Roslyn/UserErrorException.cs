namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     An expected, user-facing error raised by the host layer: a missing or ambiguous solution, an
///     unresolvable or unbuilt spec, a tampered baseline, a spec DLL with no spec class. It is shared by
///     both host surfaces — the CLI top-level handler renders the message alone to stderr and exits 2;
///     the MCP <c>GlobalCallToolFilter</c> returns it to the client as an error result without logging.
///     Anything else is treated as a bug and gets a full stack trace (CLI) or one logged warning (MCP).
/// </summary>
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
