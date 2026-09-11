using System.Text;
using System.Text.Json;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn.Baselines;

/// <summary>
///     The baseline I/O boundary of the host layer (Core owns the format and digest; this owns the disk).
/// </summary>
/// <remarks>
///     Reads a baseline file with a strict JSON walk (exact properties, a <c>schemaVersion</c>
///     <see cref="BaselineFormat.IsSupported" /> accepts, well-formed entries, each optionally carrying a
///     <c>siteCount</c> measure and a <c>because</c> attribution), then <em>recanonicalizes</em>
///     the parsed entries and re-derives the digest — a mismatch is loud tamper. A missing file
///     or missing rule section is uncaptured, not an error. Writes are canonical (fresh digest, unknown
///     sections preserved), UTF-8 no BOM, LF, and report wrote/unchanged on a CRLF-normalized compare so
///     an autocrlf checkout is a true zero-diff.
///     The file's own version governs the whole read and must reach both ends of it: the walk, because
///     <c>siteCount</c> is a stranger in a legacy file rather than an optional key, and the
///     recanonicalization, because a legacy file's stored digest was computed in the grammar of its day.
///     A write always composes the current version, so any write is also the upgrade.
///     Lives in the Roslyn host project so both the CLI and the xUnit adapter share it.
/// </remarks>
internal static class BaselineStore
{
    /// <summary>
    ///     Builds the <see cref="BaselineIndex" /> for a model's ratcheted rules — Migrate and Quarantine
    ///     containment (any rule with a <see cref="ArchRule.BaselinePath" />): resolves each rule's
    ///     baseline path against <paramref name="solutionDirectory" />, parses each distinct file once
    ///     (verifying its digest — tamper fails fast), and captures the matching section. A missing file
    ///     or missing section leaves the rule uncaptured. A scope tripwire (no baseline path) is skipped.
    /// </summary>
    public static BaselineIndex LoadForModel(ArchitectureModel model, string solutionDirectory)
    {
        var sections = new Dictionary<string, RuleBaseline>(StringComparer.Ordinal);
        var cache = new Dictionary<string, BaselineDocument?>(StringComparer.Ordinal);

        foreach (ArchRule rule in model.Rules.Where(r => r.BaselinePath is not null))
        {
            string absolutePath = ResolvePath(rule.BaselinePath!, solutionDirectory);
            if (!cache.TryGetValue(absolutePath, out BaselineDocument? document))
            {
                document = TryReadDocument(absolutePath);
                cache[absolutePath] = document;
            }

            if (document is not null && document.Sections.TryGetValue(rule.Id, out IReadOnlyList<BaselineEntry>? entries))
                sections[rule.Id] = new RuleBaseline(entries);
        }

        return new BaselineIndex(sections);
    }

    /// <summary>Reads and verifies a baseline file.</summary>
    /// <param name="absolutePath">Absolute path to the baseline file.</param>
    /// <returns>The verified document, or <see langword="null" /> when the file does not exist (uncaptured).</returns>
    /// <exception cref="UserErrorException">
    ///     The file is malformed JSON, violates the schema, or fails its digest check. The message names the
    ///     path; a digest mismatch also carries the restore hint.
    /// </exception>
    public static BaselineDocument? TryReadDocument(string absolutePath)
    {
        if (!File.Exists(absolutePath)) return null;

        string text = File.ReadAllText(absolutePath);
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw Malformed(absolutePath, ex.Message);
        }

        using (json)
        {
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Malformed(absolutePath, "the root must be a JSON object.");
            RequireExactProperties(absolutePath, root, "schemaVersion", "digest", "rules");

            int schemaVersion = ReadSchemaVersion(absolutePath, root);
            if (!BaselineFormat.IsSupported(schemaVersion))
                throw Malformed(
                    absolutePath,
                    $"unsupported schemaVersion {schemaVersion} "
                    + $"(expected {BaselineFormat.LegacySchemaVersion} or {BaselineFormat.SchemaVersion}).");

            string digest = ReadDigest(absolutePath, root);
            Dictionary<string, IReadOnlyList<BaselineEntry>> sections = ReadSections(absolutePath, root, schemaVersion);

            // Recanonicalize + verify: rebuild the digest from the parsed entries, in the grammar of the
            // version the file declares. Formatting/order/CRLF changes are invisible; any entry change is
            // not — so a mismatch is a hand edit (tamper).
            string recomputed = BaselineFormat.ComputeDigest(ToComposeInput(sections), schemaVersion);
            if (!string.Equals(recomputed, digest, StringComparison.Ordinal)) throw Tampered(absolutePath);

            return new BaselineDocument(sections);
        }
    }

    /// <summary>
    ///     Composes and writes <paramref name="document" /> canonically to <paramref name="absolutePath" />
    ///     (creating directories), UTF-8 no BOM. Reports <see cref="WriteOutcome.Unchanged" /> when the
    ///     existing file already matches on a CRLF-normalized compare, so an autocrlf checkout is not
    ///     rewritten.
    /// </summary>
    public static WriteOutcome Write(string absolutePath, BaselineDocument document)
    {
        string composed = BaselineFormat.ComposeFile(ToComposeInput(document.Sections));

        if (File.Exists(absolutePath) && NormalizeNewlines(File.ReadAllText(absolutePath)) == composed)
            return WriteOutcome.Unchanged;

        // A baseline is a committed, version-controlled file, so the write is atomic: a crash mid-write
        // must not truncate it. UTF8Encoding(false) emits no preamble, which is what keeps the file BOM-less
        // (the round-trip pins are the oracle).
        byte[] bytes = new UTF8Encoding(false).GetBytes(composed);
        AtomicFile.WriteAllBytes(absolutePath, bytes);
        return WriteOutcome.Wrote;
    }

    /// <summary>Resolves a model baseline path (forward-slash, usually relative) against the solution directory.</summary>
    public static string ResolvePath(string baselinePath, string solutionDirectory)
    {
        return Path.GetFullPath(baselinePath, solutionDirectory);
    }

    private static Dictionary<string, IReadOnlyList<BaselineEntry>> ReadSections(
        string path, JsonElement root, int schemaVersion)
    {
        var sections = new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal);
        JsonElement rules = root.GetProperty("rules");
        if (rules.ValueKind != JsonValueKind.Object) throw Malformed(path, "'rules' must be an object.");

        foreach (JsonProperty ruleProperty in rules.EnumerateObject())
        {
            JsonElement section = ruleProperty.Value;
            if (section.ValueKind != JsonValueKind.Object) throw Malformed(path, $"rule '{ruleProperty.Name}' must be an object.");
            RequireExactProperties(path, section, "entries");

            JsonElement entriesElement = section.GetProperty("entries");
            if (entriesElement.ValueKind != JsonValueKind.Array) throw Malformed(path, $"rule '{ruleProperty.Name}' entries must be an array.");

            var entries = new List<BaselineEntry>();
            foreach (JsonElement entryElement in entriesElement.EnumerateArray())
                entries.Add(ReadEntry(path, ruleProperty.Name, entryElement, schemaVersion));
            sections[ruleProperty.Name] = entries;
        }

        return sections;
    }

    private static BaselineEntry ReadEntry(string path, string ruleId, JsonElement entry, int schemaVersion)
    {
        if (entry.ValueKind != JsonValueKind.Object) throw Malformed(path, $"rule '{ruleId}' has a non-object entry.");

        // One pass over the properties answers the whole six-way shape question — {subject}, {source,
        // target}, and either of those plus a because, plus the two an edge's siteCount adds — and keeps
        // each value it meets, so nothing below walks the object a second time. This runs per entry, per
        // baseline file, on every check and status: materializing the names and set-comparing them six
        // times was work proportional to a team's whole debt ledger for a fixed question about five words.
        JsonElement? subject = null;
        JsonElement? source = null;
        JsonElement? target = null;
        JsonElement? because = null;
        JsonElement? siteCount = null;
        var hasStranger = false;
        var propertyCount = 0;
        // A legacy file has no measure, so 'siteCount' there is a stranger rather than an optional key:
        // a v1 file carrying one was hand-edited, and its digest was computed without it.
        bool measured = BaselineFormat.CarriesSiteCount(schemaVersion);

        foreach (JsonProperty property in entry.EnumerateObject())
        {
            propertyCount++;
            switch (property.Name)
            {
                case "subject":
                    subject = property.Value;
                    break;
                case "source":
                    source = property.Value;
                    break;
                case "target":
                    target = property.Value;
                    break;
                case "because":
                    because = property.Value;
                    break;
                case "siteCount" when measured:
                    siteCount = property.Value;
                    break;
                default:
                    hasStranger = true;
                    break;
            }
        }

        // Counting the distinct names back against the properties read is what rejects a repeated key: a
        // second 'subject' is a hand edit whose second value would silently never be read.
        int distinctNames = Present(subject) + Present(source) + Present(target) + Present(because) + Present(siteCount);
        bool exactlyNamed = !hasStranger && propertyCount == distinctNames;

        bool isSubject = exactlyNamed && subject is not null && source is null && target is null;
        bool isEdge = exactlyNamed && source is not null && target is not null && subject is null;
        if (!isSubject && !isEdge)
            throw Malformed(path, $"rule '{ruleId}' has an entry that is neither {{source, target}} nor {{subject}}.");

        if (isSubject && siteCount is not null)
            throw Malformed(path, $"rule '{ruleId}' has a 'siteCount' on a subject entry — only edge entries carry one.");

        BaselineEntry parsed = isSubject
            ? BaselineEntry.ForSubject(RequireNonEmptyString(path, ruleId, "subject", subject!.Value))
            : BaselineEntry.ForEdge(
                RequireNonEmptyString(path, ruleId, "source", source!.Value),
                RequireNonEmptyString(path, ruleId, "target", target!.Value));

        if (siteCount is { } count)
            parsed = parsed.WithSiteCount(ReadSiteCount(path, ruleId, count));

        return because is { } attribution
            ? parsed.WithBecause(ReadBecause(path, ruleId, attribution))
            : parsed;
    }

    private static int Present(JsonElement? property)
    {
        return property is null ? 0 : 1;
    }

    private static int ReadSiteCount(string path, string ruleId, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int siteCount) || siteCount < 1)
            throw Malformed(path, $"rule '{ruleId}' has a 'siteCount' that is not an integer of at least 1.");

        return siteCount;
    }

    private static string ReadBecause(string path, string ruleId, JsonElement value)
    {
        string text = RequireNonEmptyString(path, ruleId, "because", value);
        bool blankOrMultiline = string.IsNullOrWhiteSpace(text) || text.IndexOf('\r') >= 0 || text.IndexOf('\n') >= 0;
        if (blankOrMultiline) throw Malformed(path, $"rule '{ruleId}' has a blank or multi-line 'because'.");
        return text;
    }

    private static int ReadSchemaVersion(string path, JsonElement root)
    {
        JsonElement value = root.GetProperty("schemaVersion");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int schemaVersion))
            throw Malformed(path, "schemaVersion must be an integer.");
        return schemaVersion;
    }

    private static string ReadDigest(string path, JsonElement root)
    {
        JsonElement value = root.GetProperty("digest");
        if (value.ValueKind != JsonValueKind.String) throw Malformed(path, "digest must be a string.");

        string digest = value.GetString()!;
        if (digest.Length != 64 || !digest.All(IsLowerHex)) throw Malformed(path, "digest must be 64 lowercase hex characters.");
        return digest;
    }

    private static string RequireNonEmptyString(string path, string ruleId, string property, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
            throw Malformed(path, $"rule '{ruleId}' has an empty or non-string '{property}'.");
        return value.GetString()!;
    }

    private static void RequireExactProperties(string path, JsonElement element, params string[] expected)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (Array.IndexOf(expected, property.Name) < 0) throw Malformed(path, $"unknown property '{property.Name}'.");
            seen.Add(property.Name);
        }

        foreach (string name in expected)
            if (!seen.Contains(name))
                throw Malformed(path, $"missing property '{name}'.");
    }

    private static IReadOnlyDictionary<string, IReadOnlyCollection<BaselineEntry>> ToComposeInput(
        IReadOnlyDictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        return sections.ToDictionary(kv => kv.Key, kv => (IReadOnlyCollection<BaselineEntry>)kv.Value, StringComparer.Ordinal);
    }

    private static bool IsLowerHex(char c)
    {
        return c is >= '0' and <= '9' or >= 'a' and <= 'f';
    }

    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n");
    }

    private static UserErrorException Malformed(string path, string detail)
    {
        return new UserErrorException($"Baseline file '{path}' is not valid: {detail}");
    }

    private static UserErrorException Tampered(string path)
    {
        return new UserErrorException(
            $"Baseline file '{path}' failed its integrity check: the digest does not match the entries. " +
            "A baseline shrinks via 'loadbearing baseline --accept-reductions' and grows only via 'loadbearing baseline --add' " +
            "(one attributed entry at a time). If the file was edited by hand, restore it from version control.");
    }
}
