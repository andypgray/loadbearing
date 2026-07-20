namespace Zphil.LoadBearing.Validation;

/// <summary>
///     The spec-source position of an anchor — the file name and 1-based line where a rule, scope, or
///     member anchor was authored, captured via <c>[CallerFilePath]</c>/<c>[CallerLineNumber]</c> on the
///     anchor factories (GRAMMAR §8). Diagnostics metadata only: it never enters the reified model or any
///     render target, so the model stays location-free and deterministic by construction. Rendered
///     <em>file name only</em> — never the machine-specific full compile-time path — so goldens stay
///     byte-identical across build machines (fixture specs build in temp directories).
/// </summary>
public sealed class SpecSourceLocation
{
    internal SpecSourceLocation(string file, int line)
    {
        File = file;
        Line = line;
    }

    /// <summary>The bare source file name (no directory) the anchor was authored in.</summary>
    public string File { get; }

    /// <summary>The 1-based line of the anchor factory call.</summary>
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

    /// <summary>The rendered <c>file:line</c> prefix form (file name only).</summary>
    public override string ToString()
    {
        return $"{File}:{Line}";
    }
}