using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     The on-disk baseline format: composes a baseline file's text from the entries it should hold,
///     and computes the digest that file carries.
/// </summary>
/// <remarks>
///     A baseline file is line-oriented JSON — UTF-8 with no byte-order mark, LF line endings, a
///     trailing newline, a two-space indent, one entry object per line — so paying off one
///     grandfathered violation removes exactly one line in a diff. Rules sort by ID and entries by
///     source or subject then target, both ordinal, so the same entries always compose to the same
///     bytes. One file holds a section per rule ID, and an entry object carries <c>source</c> and
///     <c>target</c>, or <c>subject</c>, then an optional <c>siteCount</c> and an optional
///     <c>because</c>. The <c>digest</c> field is computed over a rendering of the entries rather than
///     over the file text, so reformatting the file or checking it out with CRLF endings leaves it
///     valid, while changing an entry, a count or a reason does not: a hand-edited baseline is refused
///     rather than obeyed, and the file's history is the record of who widened what.
/// </remarks>
// The digest verbs take the version they are computing for because a reader recanonicalizes a
// stored file with the file's own: LegacySchemaVersion's grammar is frozen verbatim (its preamble,
// and no siteCount line), so a file written before the measure existed still verifies. Composing
// always writes SchemaVersion, so any write is also the upgrade.
public static class BaselineFormat
{
    /// <summary>
    ///     The <c>schemaVersion</c> value <see cref="ComposeFile" /> writes, so composing a file is also
    ///     the upgrade path off <see cref="LegacySchemaVersion" />.
    /// </summary>
    public const int SchemaVersion = 2;

    /// <summary>
    ///     The older <c>schemaVersion</c> a reader still accepts: a file written before entries carried a
    ///     site count. Its entries have none, so each grandfathers its pair however many sites it grows
    ///     to, until the file is written again.
    /// </summary>
    public const int LegacySchemaVersion = 1;

    private const string DigestPreamble = "loadbearing-baseline-digest-v2";
    private const string LegacyDigestPreamble = "loadbearing-baseline-digest-v1";
    private const string HexDigits = "0123456789abcdef";

    /// <summary>
    ///     Whether a baseline file declaring <paramref name="schemaVersion" /> can be read: true for
    ///     <see cref="SchemaVersion" /> and for <see cref="LegacySchemaVersion" />, false for anything
    ///     else, which a reader should refuse rather than guess at.
    /// </summary>
    public static bool IsSupported(int schemaVersion)
    {
        return schemaVersion == SchemaVersion || schemaVersion == LegacySchemaVersion;
    }

    /// <summary>
    ///     Whether an entry in a baseline file declaring <paramref name="schemaVersion" /> may carry a
    ///     <c>siteCount</c>: true for every accepted version but <see cref="LegacySchemaVersion" />, whose
    ///     entries predate the count.
    /// </summary>
    // The one answer both the digest grammar and a reader's property walk take, so the two cannot
    // disagree about which keys a version admits.
    public static bool CarriesSiteCount(int schemaVersion)
    {
        return schemaVersion != LegacySchemaVersion;
    }

    /// <summary>
    ///     Composes the whole text of a baseline file holding the given entries, keyed by rule ID: sorts
    ///     the rules and their entries into the file's canonical order, computes a fresh <c>digest</c>
    ///     over them, and returns the file, newline-terminated and ready to write as UTF-8 with no
    ///     byte-order mark. The order of the input and any duplicate entries in it make no difference to
    ///     the result. The file is always <see cref="SchemaVersion" />, so writing one read at
    ///     <see cref="LegacySchemaVersion" /> upgrades it.
    /// </summary>
    public static string ComposeFile(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        IReadOnlyList<SortedRule> sorted = SortRules(rules);
        string digest = ComputeDigest(sorted, SchemaVersion);

        var builder = new StringBuilder();
        builder.Append("{\n");
        builder.Append("  \"schemaVersion\": ").Append(SchemaVersion).Append(",\n");
        builder.Append("  \"digest\": ").Append(Quote(digest)).Append(",\n");

        if (sorted.Count == 0)
        {
            builder.Append("  \"rules\": {}\n");
        }
        else
        {
            builder.Append("  \"rules\": {\n");
            for (var i = 0; i < sorted.Count; i++)
            {
                builder.Append("    ").Append(Quote(sorted[i].Id)).Append(": {\n");
                AppendEntries(builder, sorted[i].Entries);
                builder.Append(i < sorted.Count - 1 ? "    },\n" : "    }\n");
            }

            builder.Append("  }\n");
        }

        builder.Append("}\n");
        return builder.ToString();
    }

    /// <summary>
    ///     The exact text <c>ComputeDigest</c> hashes, in <see cref="SchemaVersion" />'s grammar: a fixed
    ///     preamble line, then a <c>rule &lt;id&gt;</c> line per rule and an
    ///     <c>edge &lt;source&gt; -&gt; &lt;target&gt;</c> or <c>subject &lt;id&gt;</c> line per entry, in
    ///     the file's canonical order. An entry with a site count adds a <c>siteCount &lt;n&gt;</c> line
    ///     after its own, and one with a reason a <c>because &lt;text&gt;</c> line after that. Every line
    ///     ends in LF, the last one included. Useful for showing what a digest was taken over when a
    ///     stored file fails to verify.
    /// </summary>
    public static string DigestInput(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        return DigestInput(rules, SchemaVersion);
    }

    /// <summary>
    ///     The exact text <c>ComputeDigest</c> hashes for a file declaring
    ///     <paramref name="schemaVersion" /> — what to compute when verifying a stored file, passing the
    ///     version that file declares. A fixed preamble line, then a <c>rule &lt;id&gt;</c> line per rule
    ///     and an <c>edge &lt;source&gt; -&gt; &lt;target&gt;</c> or <c>subject &lt;id&gt;</c> line per
    ///     entry, in the file's canonical order, each entry followed by a <c>siteCount &lt;n&gt;</c> line
    ///     where it has a count and a <c>because &lt;text&gt;</c> line where it has a reason. Every line
    ///     ends in LF, the last one included. <see cref="LegacySchemaVersion" /> renders in the grammar it
    ///     shipped with (its own preamble, and no <c>siteCount</c> line), so a file written before the
    ///     count existed still verifies against the digest it carries. A version
    ///     <see cref="IsSupported" /> does not accept renders in the current grammar.
    /// </summary>
    public static string DigestInput(
        IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules, int schemaVersion)
    {
        return DigestInput(SortRules(rules), schemaVersion);
    }

    /// <summary>
    ///     The digest a baseline file holding the given entries carries: the SHA-256 of the UTF-8 bytes of
    ///     <see cref="DigestInput(IReadOnlyDictionary{string, IReadOnlyCollection{BaselineEntry}})" />, in
    ///     lowercase hex, in <see cref="SchemaVersion" />'s grammar. Taken over the entries rather than
    ///     over the file text, so a file that differs only in layout or line endings still verifies.
    /// </summary>
    public static string ComputeDigest(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        return ComputeDigest(rules, SchemaVersion);
    }

    /// <summary>
    ///     The digest a baseline file declaring <paramref name="schemaVersion" /> carries: the SHA-256 of
    ///     the UTF-8 bytes of
    ///     <see cref="DigestInput(IReadOnlyDictionary{string, IReadOnlyCollection{BaselineEntry}}, int)" />,
    ///     in lowercase hex. This is what to recompute when verifying a stored file, passing the version
    ///     that file declares; a result differing from the file's own <c>digest</c> field means the file
    ///     was edited by hand rather than written by <see cref="ComposeFile" />.
    /// </summary>
    public static string ComputeDigest(
        IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules, int schemaVersion)
    {
        return ComputeDigest(SortRules(rules), schemaVersion);
    }

    private static string ComputeDigest(IReadOnlyList<SortedRule> sorted, int schemaVersion)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(DigestInput(sorted, schemaVersion)));
        return ToLowerHex(hash);
    }

    private static string DigestInput(IReadOnlyList<SortedRule> sorted, int schemaVersion)
    {
        bool counted = CarriesSiteCount(schemaVersion);
        var builder = new StringBuilder();
        builder.Append(counted ? DigestPreamble : LegacyDigestPreamble).Append('\n');
        foreach (SortedRule rule in sorted)
        {
            builder.Append("rule ").Append(rule.Id).Append('\n');
            foreach (BaselineEntry entry in rule.Entries)
            {
                if (entry.Subject is { } subject)
                    builder.Append("subject ").Append(subject).Append('\n');
                else
                    builder.Append("edge ").Append(entry.Source).Append(" -> ").Append(entry.Target).Append('\n');
                if (counted && entry.SiteCount is { } siteCount)
                    builder.Append("siteCount ").Append(Digits(siteCount)).Append('\n');
                if (entry.Because is { } because)
                    builder.Append("because ").Append(because).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static void AppendEntries(StringBuilder builder, IReadOnlyList<BaselineEntry> entries)
    {
        if (entries.Count == 0)
        {
            builder.Append("      \"entries\": []\n");
            return;
        }

        builder.Append("      \"entries\": [\n");
        for (var i = 0; i < entries.Count; i++)
        {
            BaselineEntry entry = entries[i];
            builder.Append("        ");
            if (entry.Subject is { } subject)
                builder.Append("{ \"subject\": ").Append(Quote(subject));
            else
                builder.Append("{ \"source\": ").Append(Quote(entry.Source!)).Append(", \"target\": ")
                    .Append(Quote(entry.Target!));
            if (entry.SiteCount is { } siteCount)
                builder.Append(", \"siteCount\": ").Append(Digits(siteCount));
            if (entry.Because is { } because)
                builder.Append(", \"because\": ").Append(Quote(because));
            builder.Append(" }");
            builder.Append(i < entries.Count - 1 ? ",\n" : "\n");
        }

        builder.Append("      ]\n");
    }

    private static IReadOnlyList<SortedRule> SortRules(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        return Guard.NotNull(rules, nameof(rules))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new SortedRule(kv.Key, BaselineEntry.InCanonicalOrder(kv.Value)))
            .ToList();
    }

    // Invariant digits for the one number the format writes into an entry. The culture-sensitive
    // default would only bite on a negative sign, which a site count never carries — but the file is a
    // wire format read on machines far from the one that wrote it, and the digest is over these bytes.
    private static string Digits(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string ToLowerHex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = HexDigits[bytes[i] >> 4];
            chars[i * 2 + 1] = HexDigits[bytes[i] & 0xF];
        }

        return new string(chars);
    }

    // Minimal JSON string escaper: quote, backslash, and control chars. Symbol IDs and rule IDs never
    // need it in practice, but the format stays honest and the escape path is pinned by test.
    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char c in value)
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(c);
                    break;
            }

        builder.Append('"');
        return builder.ToString();
    }

    private readonly struct SortedRule(string id, IReadOnlyList<BaselineEntry> entries)
    {
        public string Id { get; } = id;
        public IReadOnlyList<BaselineEntry> Entries { get; } = entries;
    }
}
