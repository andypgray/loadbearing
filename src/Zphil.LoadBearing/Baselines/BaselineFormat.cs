using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     The on-disk baseline format: composes a baseline file's text from the entries it should hold, and
///     computes the seal each entry in that file carries.
/// </summary>
/// <remarks>
///     <para>
///         A baseline file is line-oriented JSON — UTF-8 with no byte-order mark, LF line endings, a
///         trailing newline, a two-space indent, one entry object per line — so paying off one
///         grandfathered violation removes exactly one line in a diff. Rules sort by ID and entries by
///         source or subject then target, both ordinal, so the same entries always compose to the same
///         bytes. One file holds a section per rule ID, and an entry object carries <c>source</c> and
///         <c>target</c>, or <c>subject</c>, then an optional <c>siteCount</c>, an optional
///         <c>because</c>, and a <c>seal</c> last.
///     </para>
///     <para>
///         The <c>seal</c> is computed over the rule ID and a rendering of the entry's own fields rather
///         than over the file text, so reformatting the file or checking it out with CRLF endings leaves
///         it valid, while editing an entry, a count or a reason does not: a hand-edited baseline is
///         refused rather than obeyed, and the file's history is the record of who widened what. The rule
///         ID is sealed in too, so an entry moved from one rule's section to another's fails there. What
///         a seal cannot see is a whole line taken away: deleting an entry, or a section, is accepted,
///         and the violation it grandfathered simply goes red on the next check. This is tamper-evident,
///         not tamper-proof — it catches accidents and hand edits, and review of the file's diff remains
///         the human gate.
///     </para>
///     <para>
///         Sealing each entry rather than the whole file is also what lets two branches burn the same
///         baseline down at once. Reductions of different rules have nothing in common to conflict on.
///         Reductions of one rule can still collide on adjacent lines, a diff being line-based — but
///         every line that survives such a merge is a complete, self-sealed entry, so resolving it by
///         keeping whole lines from either side yields a file the tool accepts with nothing to re-run.
///     </para>
/// </remarks>
// The whole-file digest is a v1-only concern, which is what LegacyDigest's name says: that grammar is
// frozen verbatim (its own preamble, and no siteCount line) so a file written before either measure
// existed still verifies against the digest it stored. Composing always writes SchemaVersion, so any
// write is also the upgrade.
public static class BaselineFormat
{
    /// <summary>
    ///     The <c>schemaVersion</c> value <see cref="ComposeFile" /> writes, so composing a file is also
    ///     the upgrade path off <see cref="LegacySchemaVersion" />.
    /// </summary>
    public const int SchemaVersion = 2;

    /// <summary>
    ///     The older <c>schemaVersion</c> a reader still accepts: a file written before entries carried a
    ///     site count or a seal. Its entries have no count, so each grandfathers its pair however many
    ///     sites it grows to, and one whole-file <c>digest</c> stands in for the per-entry seals, until
    ///     the file is written again.
    /// </summary>
    public const int LegacySchemaVersion = 1;

    // Sixteen hex characters — 64 bits of the hash — rather than all sixty-four: a seal rides on every
    // entry line, and the line has to stay readable by whoever resolves a merge conflict over it. Neither
    // length hides anything, since a seal is recomputable from the entry beside it; the bar this clears is
    // the accident and the hand edit, and 64 bits clears it by a wide margin.
    internal const int SealLength = 16;
    internal const int SealBytes = SealLength / 2;

    private const string SealPreamble = "loadbearing-baseline-seal-v2";
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
    // The one answer both the frozen legacy digest grammar and a reader's property walk take, so the two
    // cannot disagree about which keys a version admits.
    public static bool CarriesSiteCount(int schemaVersion)
    {
        return schemaVersion != LegacySchemaVersion;
    }

    /// <summary>
    ///     Whether every entry in a baseline file declaring <paramref name="schemaVersion" /> carries a
    ///     <c>seal</c>: true for every accepted version but <see cref="LegacySchemaVersion" />, whose
    ///     entries are covered by one whole-file <c>digest</c> instead. Where it is carried it is
    ///     required — an entry without one is malformed, not unsealed.
    /// </summary>
    // Same one-owner reason as CarriesSiteCount: the composer and a reader's property walk take this
    // answer rather than each deciding for itself.
    public static bool CarriesSeal(int schemaVersion)
    {
        return schemaVersion != LegacySchemaVersion;
    }

    /// <summary>
    ///     Composes the whole text of a baseline file holding the given entries, keyed by rule ID: sorts
    ///     the rules and their entries into the file's canonical order, computes a fresh <c>seal</c> for
    ///     each entry, and returns the file, newline-terminated and ready to write as UTF-8 with no
    ///     byte-order mark. The order of the input and any duplicate entries in it make no difference to
    ///     the result. The file is always <see cref="SchemaVersion" />, so writing one read at
    ///     <see cref="LegacySchemaVersion" /> upgrades it.
    /// </summary>
    public static string ComposeFile(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        IReadOnlyList<SortedRule> sorted = SortRules(rules);

        using var sealer = new BaselineSealer();
        var builder = new StringBuilder();
        builder.Append("{\n");
        builder.Append("  \"schemaVersion\": ").Append(SchemaVersion).Append(",\n");

        if (sorted.Count == 0)
        {
            builder.Append("  \"rules\": {}\n");
        }
        else
        {
            builder.Append("  \"rules\": {\n");
            for (var i = 0; i < sorted.Count; i++)
            {
                SortedRule rule = sorted[i];
                builder.Append("    ").Append(Quote(rule.Id)).Append(": {\n");
                AppendEntries(builder, sealer, rule);
                builder.Append(i < sorted.Count - 1 ? "    },\n" : "    }\n");
            }

            builder.Append("  }\n");
        }

        builder.Append("}\n");
        return builder.ToString();
    }

    /// <summary>
    ///     The exact text an entry's <c>seal</c> is taken over: a fixed preamble line, then a
    ///     <c>rule &lt;id&gt;</c> line naming the section the entry sits in, an
    ///     <c>edge &lt;source&gt; -&gt; &lt;target&gt;</c> or <c>subject &lt;id&gt;</c> line for the entry
    ///     itself, a <c>siteCount &lt;n&gt;</c> line where it has a count, and a
    ///     <c>because &lt;text&gt;</c> line where it has a reason. Every line ends in LF, the last one
    ///     included. Useful for showing what a seal was taken over when a stored entry fails to verify.
    /// </summary>
    public static string SealInput(string ruleId, BaselineEntry entry)
    {
        string id = Guard.NotNull(ruleId, nameof(ruleId));
        BaselineEntry content = Guard.NotNull(entry, nameof(entry));

        var builder = new StringBuilder();
        builder.Append(SealPreamble).Append('\n');
        builder.Append("rule ").Append(id).Append('\n');
        AppendEntryLines(builder, content, counted: true);
        return builder.ToString();
    }

    /// <summary>
    ///     The <c>seal</c> an entry carries in the section for <paramref name="ruleId" />: the first 16
    ///     lowercase hex characters of the SHA-256 of the UTF-8 bytes of <see cref="SealInput" />.
    ///     Recompute it to verify a stored entry — a result differing from the entry's own <c>seal</c>
    ///     means the line was edited by hand rather than written by <see cref="ComposeFile" />.
    /// </summary>
    public static string ComputeSeal(string ruleId, BaselineEntry entry)
    {
        using var sealer = new BaselineSealer();
        return sealer.Seal(ruleId, entry);
    }

    /// <summary>
    ///     The exact text <see cref="LegacyDigest" /> hashes, in <see cref="LegacySchemaVersion" />'s
    ///     frozen grammar: a fixed preamble line, then a <c>rule &lt;id&gt;</c> line per rule and an
    ///     <c>edge &lt;source&gt; -&gt; &lt;target&gt;</c> or <c>subject &lt;id&gt;</c> line per entry, in
    ///     the file's canonical order, each entry followed by a <c>because &lt;text&gt;</c> line where it
    ///     has a reason. No <c>siteCount</c> line, whatever the entries carry: that version predates the
    ///     count. Every line ends in LF, the last one included. Useful for showing what a digest was taken
    ///     over when a stored file of that version fails to verify.
    /// </summary>
    public static string LegacyDigestInput(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        IReadOnlyList<SortedRule> sorted = SortRules(rules);
        var builder = new StringBuilder();
        builder.Append(LegacyDigestPreamble).Append('\n');
        foreach (SortedRule rule in sorted)
        {
            builder.Append("rule ").Append(rule.Id).Append('\n');
            foreach (BaselineEntry entry in rule.Entries) AppendEntryLines(builder, entry, counted: false);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     The whole-file <c>digest</c> a <see cref="LegacySchemaVersion" /> baseline carries: the SHA-256
    ///     of the UTF-8 bytes of <see cref="LegacyDigestInput" />, in lowercase hex. This is what to
    ///     recompute when verifying a stored file that declares that version; a result differing from the
    ///     file's own <c>digest</c> field means the file was edited by hand. A file
    ///     <see cref="ComposeFile" /> writes carries no whole-file digest — each of its entries carries
    ///     its own <see cref="ComputeSeal" /> instead.
    /// </summary>
    public static string LegacyDigest(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        string input = LegacyDigestInput(rules);
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return ToLowerHex(hash, hash.Length);
    }

    // The one place that says what an entry's content is, so a seal and the frozen legacy digest cannot
    // disagree about it. All that separates the two renderings is the preamble, whether the rule ID leads
    // one entry or a whole section, and whether the measure is part of the grammar at all.
    internal static void AppendEntryLines(StringBuilder builder, BaselineEntry entry, bool counted)
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

    internal static string ToLowerHex(byte[] bytes, int byteCount)
    {
        var chars = new char[byteCount * 2];
        for (var i = 0; i < byteCount; i++)
        {
            chars[i * 2] = HexDigits[bytes[i] >> 4];
            chars[i * 2 + 1] = HexDigits[bytes[i] & 0xF];
        }

        return new string(chars);
    }

    private static void AppendEntries(StringBuilder builder, BaselineSealer sealer, SortedRule rule)
    {
        IReadOnlyList<BaselineEntry> entries = rule.Entries;
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
            string seal = sealer.Seal(rule.Id, entry);
            builder.Append(", \"seal\": ").Append(Quote(seal));
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
    // wire format read on machines far from the one that wrote it, and the seal is over these bytes.
    private static string Digits(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
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
