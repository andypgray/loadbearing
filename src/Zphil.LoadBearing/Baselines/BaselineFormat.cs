using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     The canonical on-disk baseline format and its integrity digest (DESIGN.md §13(d), Phase 5). A
///     baseline file is line-oriented JSON — UTF-8 no BOM, LF endings, a trailing newline, 2-space
///     indent, one entry object per line — so a burndown diff removes exactly one line. An entry may
///     carry an optional <c>because</c> attribution as its last property, folded into the digest.
///     Rules sort ordinal by ID; entries sort ordinal by <c>((Source ?? Subject), (Target ?? ""))</c>. The
///     <c>digest</c> is SHA-256 over a separate line-oriented rendering of the parsed entries
///     (<see cref="DigestInput" />), so formatting or line-ending changes (an autocrlf checkout) are
///     invisible while entry changes are not — tamper-<em>evident</em>, with git review the human gate.
/// </summary>
public static class BaselineFormat
{
    /// <summary>The baseline file schema version.</summary>
    public const int SchemaVersion = 1;

    private const string DigestPreamble = "loadbearing-baseline-digest-v1";
    private const string HexDigits = "0123456789abcdef";

    /// <summary>
    ///     Composes the canonical file bytes-as-string for the given rule sections: sorts rules and
    ///     entries, computes and embeds a fresh <c>digest</c>, and emits the line-oriented JSON. The
    ///     input's own order and duplicates do not matter (the composer sorts; upstream stores dedupe).
    /// </summary>
    public static string ComposeFile(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        var sorted = SortRules(rules);
        string digest = ComputeDigest(sorted);

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
    ///     The line-oriented digest input for the given rules (Spec 1): a fixed preamble line, then a
    ///     <c>rule &lt;id&gt;</c> line per rule (ordinal) and an <c>edge &lt;src&gt; -&gt; &lt;tgt&gt;</c>
    ///     or <c>subject &lt;id&gt;</c> line per entry (tuple-sorted). An attributed entry adds a
    ///     <c>because &lt;text&gt;</c> line immediately after its own line; the encoding stays injective
    ///     because digest-input lines only ever start with <c>rule </c>/<c>edge </c>/<c>subject </c>/
    ///     <c>because </c> and because-text is single-line by invariant. Every line is LF-terminated,
    ///     including the last. Exposed for the self-verifying digest test.
    /// </summary>
    public static string DigestInput(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        return DigestInput(SortRules(rules));
    }

    /// <summary>The SHA-256 lowercase-hex digest over <see cref="DigestInput" /> (UTF-8 bytes).</summary>
    public static string ComputeDigest(IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> rules)
    {
        return ComputeDigest(SortRules(rules));
    }

    private static string ComputeDigest(IReadOnlyList<SortedRule> sorted)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(DigestInput(sorted)));
        return ToLowerHex(hash);
    }

    private static string DigestInput(IReadOnlyList<SortedRule> sorted)
    {
        var builder = new StringBuilder();
        builder.Append(DigestPreamble).Append('\n');
        foreach (SortedRule rule in sorted)
        {
            builder.Append("rule ").Append(rule.Id).Append('\n');
            foreach (BaselineEntry entry in rule.Entries)
            {
                if (entry.Subject is { } subject)
                    builder.Append("subject ").Append(subject).Append('\n');
                else
                    builder.Append("edge ").Append(entry.Source).Append(" -> ").Append(entry.Target).Append('\n');
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
            .Select(kv => new SortedRule(kv.Key, SortEntries(kv.Value)))
            .ToList();
    }

    private static IReadOnlyList<BaselineEntry> SortEntries(IReadOnlyCollection<BaselineEntry> entries)
    {
        return entries
            .OrderBy(e => e.Source ?? e.Subject, StringComparer.Ordinal)
            .ThenBy(e => e.Target ?? string.Empty, StringComparer.Ordinal)
            .ToList();
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