namespace Zphil.LoadBearing.Rendering;

/// <summary>Whether a file write changed the target file's bytes.</summary>
public enum WriteOutcome
{
    /// <summary>The write changed the target file's bytes.</summary>
    Wrote,

    /// <summary>The target file's bytes were already current; no change was made.</summary>
    Unchanged
}