using System.Text;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The byte-level file adapter around the pure Core <see cref="ManagedBlock" /> splicer. It
///     reads raw bytes (not <c>File.ReadAllText</c>, which silently strips a BOM), decodes them as
///     <em>strict</em> UTF-8 (a file that is not valid UTF-8 is refused, never rewritten with U+FFFD
///     replacement characters), preserves the file's BOM state exactly, writes new files as UTF-8
///     without a BOM, and reports wrote/unchanged by comparing final bytes to existing bytes (so an
///     unchanged spec re-render is a true zero-diff). The write itself goes through
///     <see cref="AtomicFile" />, so a crash mid-render leaves the old committed <c>AGENTS.md</c> intact,
///     never a truncated one. A malformed managed block or a non-UTF-8 file becomes a
///     <see cref="UserErrorException" /> that names the file — LoadBearing never repairs a broken file,
///     and never destroys one it cannot cleanly round-trip.
/// </summary>
internal static class ManagedBlockFile
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    // Strict UTF-8: invalid bytes throw DecoderFallbackException on read (refused, not silently replaced
    // with U+FFFD), and no preamble is emitted on write (the BOM is prepended by hand below). GetBytes
    // over a valid string is byte-identical to the prior new UTF8Encoding(false), so output does not move.
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WriteOutcome Splice(string path, string body)
    {
        byte[]? existingBytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
        bool hasBom = existingBytes is not null && StartsWithBom(existingBytes);
        string? existingText = Decode(path, existingBytes, hasBom);

        string newText;
        try
        {
            newText = ManagedBlock.Splice(existingText, body);
        }
        catch (MalformedManagedBlockException ex)
        {
            throw new UserErrorException($"{path}: {ex.Message}", ex);
        }

        // Encoding.GetBytes never emits the preamble, so prepend the BOM bytes explicitly when the
        // existing file carried one (new files stay BOM-free).
        byte[] textBytes = StrictUtf8.GetBytes(newText);
        byte[] newBytes = hasBom ? [.. Utf8Bom, .. textBytes] : textBytes;
        if (existingBytes is not null && newBytes.AsSpan().SequenceEqual(existingBytes)) return WriteOutcome.Unchanged;

        AtomicFile.WriteAllBytes(path, newBytes);
        return WriteOutcome.Wrote;
    }

    private static bool StartsWithBom(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2];
    }

    // Decode UTF-8 text, skipping a leading BOM so it never enters the string the splicer sees (the
    // adapter re-emits it on write). Absent file → null (the splicer's "absent" case). A non-UTF-8 file is
    // refused rather than decoded lossily: splicing into mojibake would commit the corruption on write.
    private static string? Decode(string path, byte[]? bytes, bool hasBom)
    {
        if (bytes is null) return null;

        // A UTF-16 file (BOM FF FE / FE FF) is invalid UTF-8 at byte 0; name the encoding specifically so
        // the fix is obvious (convert it) rather than a generic "invalid bytes" that reads as damage.
        if (LooksLikeUtf16(bytes))
            throw new UserErrorException(
                $"{path}: looks like UTF-16 (a leading byte-order mark). LoadBearing manages UTF-8 files only; "
                + "convert it to UTF-8 and re-render.");

        int offset = hasBom ? Utf8Bom.Length : 0;
        try
        {
            return StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException ex)
        {
            throw new UserErrorException(
                $"{path}: not valid UTF-8. LoadBearing manages a UTF-8 AGENTS.md block and refuses a file it "
                + "cannot decode rather than rewriting it with replacement characters; convert it to UTF-8 and re-render.",
                ex);
        }
    }

    private static bool LooksLikeUtf16(byte[] bytes)
    {
        return bytes.Length >= 2
               && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF));
    }
}