using System.Text;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     The byte-level file adapter around the pure Core <see cref="ManagedBlock" /> splicer (R1). It
///     reads raw bytes (not <c>File.ReadAllText</c>, which silently strips a BOM), preserves the file's
///     BOM state exactly, writes new files as UTF-8 without a BOM, and reports wrote/unchanged by
///     comparing final bytes to existing bytes (so an unchanged spec re-render is a true zero-diff). A
///     malformed managed block is turned into a <see cref="UserErrorException" /> that names the file —
///     LoadBearing never repairs a broken file.
/// </summary>
internal static class ManagedBlockFile
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static WriteOutcome Splice(string path, string body)
    {
        byte[]? existingBytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
        bool hasBom = existingBytes is not null && StartsWithBom(existingBytes);
        string? existingText = Decode(existingBytes, hasBom);

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
        byte[] textBytes = new UTF8Encoding(false).GetBytes(newText);
        byte[] newBytes = hasBom ? [.. Utf8Bom, .. textBytes] : textBytes;
        if (existingBytes is not null && newBytes.AsSpan().SequenceEqual(existingBytes)) return WriteOutcome.Unchanged;

        File.WriteAllBytes(path, newBytes);
        return WriteOutcome.Wrote;
    }

    private static bool StartsWithBom(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2];
    }

    // Decode UTF-8 text, skipping a leading BOM so it never enters the string the splicer sees (the
    // adapter re-emits it on write). Absent file → null (the splicer's "absent" case).
    private static string? Decode(byte[]? bytes, bool hasBom)
    {
        if (bytes is null) return null;

        int offset = hasBom ? Utf8Bom.Length : 0;
        return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
    }
}