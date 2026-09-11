namespace Zphil.LoadBearing.Validation;

/// <summary>
///     Where in the spec source a validation error's <c>arch.Rule</c>, <c>arch.Scope</c> or
///     <c>arch.Member</c> call was written: a file name and a 1-based line, so each error is somewhere to
///     jump to. The file name alone, never a directory.
/// </summary>
// Diagnostics metadata only: it never enters the model or any render target, so the model stays
// location-free and deterministic. File name only — never the machine-specific full compile-time
// path — so goldens stay byte-identical across build machines (fixture specs build in temp
// directories).
public sealed class SpecSourceLocation
{
    internal SpecSourceLocation(string file, int line)
    {
        File = file;
        Line = line;
    }

    /// <summary>Gets the name of the source file the call was written in, without a directory.</summary>
    public string File { get; }

    /// <summary>Gets the 1-based line the call was written on.</summary>
    public int Line { get; }

    // Strip to the bare file name at the capture seam so no machine-specific directory is ever retained.
    // Both separators are trimmed by hand rather than via Path.GetFileName, which on a non-Windows host does
    // not treat '\' as a separator — a spec DLL built on Windows must render identically when checked on
    // macOS/Linux. A null/empty path (a caller that supplied no [CallerFilePath] — the pre-caller-info
    // degradation seam) yields null, so the error renders today's message with no location prefix.
    internal static SpecSourceLocation? Capture(string? filePath, int line)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        int separator = filePath!.LastIndexOfAny(['/', '\\']);
        string file = separator < 0 ? filePath : filePath.Substring(separator + 1);
        return new SpecSourceLocation(file, line);
    }

    /// <summary>Returns <c>file:line</c>, the form a validation error message opens with.</summary>
    public override string ToString()
    {
        return $"{File}:{Line}";
    }
}
