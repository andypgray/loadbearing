namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Thrown when a target file's managed markers are in a state the splicer refuses to touch:
///     a begin without an end, an end before a begin, or a duplicate begin or end (R1). LoadBearing
///     never "repairs" a broken file — the splice is abandoned with no write. The CLI file adapter
///     wraps this in a user-facing error that names the file and exits 2.
/// </summary>
public sealed class MalformedManagedBlockException : Exception
{
    /// <summary>Creates the exception with a description of the malformed marker state.</summary>
    public MalformedManagedBlockException(string message) : base(message)
    {
    }
}