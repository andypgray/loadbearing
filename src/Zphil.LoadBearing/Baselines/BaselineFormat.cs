using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>The canonical on-disk baseline format and its integrity digest.</summary>
/// <remarks>
///     A baseline file is line-oriented JSON — UTF-8 no BOM, LF endings, a trailing newline, 2-space
///     indent, one entry object per line — so a burndown diff removes exactly one line. An edge entry
///     may carry a <c>siteCount</c> measure and any entry an optional <c>because</c> attribution, in
///     that order after the ID slots; both are folded into the digest.
///     Rules sort ordinal by ID; entries sort ordinal by <c>((Source ?? Subject), (Target ?? ""))</c>. The
///     <c>digest</c> is SHA-256 over a separate line-oriented rendering of the parsed entries
///     (<see cref="DigestInput(IReadOnlyDictionary{string, IReadOnlyCollection{BaselineEntry}})" />), so
///     formatting or line-ending changes (an autocrlf checkout) are
///     invisible while entry changes are not — tamper-<em>evident</em>, with git review the human gate.
///     Composing always writes <see cref="SchemaVersion" />; <see cref="LegacySchemaVersion" /> is read
///     only, and its digest grammar is frozen verbatim so a file written before the measure existed
///     still verifies. That is why the digest verbs take the version they are computing for: a reader
///     recanonicalizes with the file's own.
/// </remarks>
public static class BaselineFormat
{
    /// <summary>The baseline file schema version every write composes.</summary>
    public const int SchemaVersion = 2;

    /// <summary>The older schema version readers still accept — a file with no site counts.</summary>
    public const int LegacySchemaVersion = 1;

    private const string DigestPreamble = "loadbearing-baseline-digest-v2";
    private const string LegacyDigestPreamble = "loadbearing-baseline-digest-v1";
    private const string HexDigits = "0123456789abcdef";

    /// <summary>Whether a reader accepts <paramref name="schemaVersion" /> — the current one, or the legacy one.</summary>
    public static bool IsSupported(int schemaVersion)
    {
        return schemaVersion == SchemaVersion || schemaVersion == LegacySchemaVersion;
    }

    /// <summary>
    ///     Whether an edge entry in a file of <paramref name="schemaVersion" /> may carry a <c>siteCount</c>
    ///     — every version but <see cref="LegacySchemaVersion" />, whose grammar predates the measure. The
    ///     one answer both the digest and a reader's property walk take, so the two cannot disagree about
    ///     which keys a version admits.
    /// </summary>
    public static bool CarriesSiteCount(int schemaVersion)
    {
        return schemaVersion != LegacySchemaVersion;
    }

    /// <summary>
    ///     Composes the canonical file bytes-as-string for the given rule sections: sorts rules and
    ///     entries, computes and embeds a fresh <c>digest</c>, and emits the line-oriented JSON. The
    ///     input's own order and duplicates do not matter. Always <see cref="SchemaVersion" />, so a
    ///     write is also the upgrade path off <see cref="LegacySchemaVersion" />.
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
    ///     The line-oriented digest input for the given rules: a fixed preamble line, then a
    ///     <c>rule &lt;id&gt;</c> line per rule (ordinal) and an <c>edge &lt;src&gt; -&gt; &lt;tgt&gt;</c>
    ///     or <c>subject &lt;id&gt;</c> line per entry (tuple-sorted).
    /// </summary>
    /// <remarks>
    ///     A counted edge entry adds a <c>siteCount &lt;n&gt;</c> line immediately after its own line, and
    ///     an attributed entry a <c>because &lt;text&gt;</c> line after that; the encoding stays injective
    ///     because digest-input lines only ever start with
    ///     <c>rule </c>/<c>edge </c>/<c>subject </c>/<c>siteCount </c>/<c>because </c> and because-text is
    ///     single-line by invariant. Every line is LF-terminated, including the last.
    /// </remarks>
    public static string DigestInput(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        return DigestInput(rules, SchemaVersion);
    }

    /// <summary>
    ///     The same digest input in <paramref name="schemaVersion" />'s grammar — what a reader
    ///     recanonicalizing a stored file computes, with the file's own version.
    /// </summary>
    /// <remarks>
    ///     <see cref="LegacySchemaVersion" />'s grammar is frozen verbatim: its preamble is the one it
    ///     shipped with and it emits no <c>siteCount</c> line, so a file written before the measure
    ///     existed still verifies against its stored digest. Callers pass a version
    ///     <see cref="IsSupported" /> accepts; anything else reads as the current grammar.
    /// </remarks>
    public static string DigestInput(
        IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules, int schemaVersion)
    {
        return DigestInput(SortRules(rules), schemaVersion);
    }

    /// <summary>
    ///     The SHA-256 lowercase-hex digest over
    ///     <see cref="DigestInput(IReadOnlyDictionary{string, IReadOnlyCollection{BaselineEntry}})" />
    ///     (UTF-8 bytes).
    /// </summary>
    public static string ComputeDigest(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        return ComputeDigest(rules, SchemaVersion);
    }

    /// <summary>
    ///     The SHA-256 lowercase-hex digest over
    ///     <see cref="DigestInput(IReadOnlyDictionary{string, IReadOnlyCollection{BaselineEntry}}, int)" />
    ///     (UTF-8 bytes) — the verb a reader verifies a stored file with, passing that file's version.
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
