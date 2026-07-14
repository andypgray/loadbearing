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
    public UserErrorException(string message) : base(message)
    {
    }

    public UserErrorException(string message, Exception innerException) : base(message, innerException)
    {
    }
}