using System.Text;
using System.Text.Json;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn.Baselines;

/// <summary>
///     The baseline I/O boundary of the host layer (Core owns the format and its seals; this owns the disk).
/// </summary>
/// <remarks>
///     Reads a baseline file with a strict JSON walk (exact properties, a <c>schemaVersion</c>
///     <see cref="BaselineFormat.IsSupported" /> accepts, well-formed entries, each optionally carrying a
///     <c>siteCount</c> measure and a <c>because</c> attribution), verifying each entry's <c>seal</c> as it
///     is read — a mismatch is loud tamper, naming that entry. A missing file or missing rule section is
///     uncaptured, not an error. Writes are canonical (fresh seals, unknown sections preserved), UTF-8 no
///     BOM, LF, and report wrote/unchanged on a CRLF-normalized compare so an autocrlf checkout is a true
///     zero-diff.
///     The file's own version governs the whole read and must reach both ends of it: the walk, because
///     <c>siteCount</c> and <c>seal</c> are strangers in a legacy file rather than keys it admits, and the
///     integrity check, because a legacy file has no per-entry seals — it carries one whole-file
///     <c>digest</c>, computed in the grammar of its day, which is <em>recanonicalized</em> from the parsed
///     entries and compared instead. A write always composes the current version, so any write is also the
///     upgrade.
/// </remarks>
internal static class BaselineStore
{
    /// <summary>
    ///     Builds the <see cref="BaselineIndex" /> for a model's ratcheted rules — Migrate and Quarantine
    ///     containment (any rule with a <see cref="ArchRule.BaselinePath" />): resolves each rule's
    ///     baseline path against <paramref name="solutionDirectory" />, parses each distinct file once
    ///     (verifying its integrity — tamper fails fast), and captures the matching section.
    /// </summary>
    /// <remarks>
    ///     A missing file or missing section leaves the rule uncaptured. A scope tripwire (no baseline
    ///     path) is skipped.
    /// </remarks>
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
    ///     The file is malformed JSON, violates the schema, or fails its integrity check. The message names
    ///     the path; an integrity failure also carries the recovery hint, and under the current version the
    ///     entry that failed.
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

            // The version leads the read, because the root's own property set depends on it — a legacy file
            // carries a whole-file digest and a current one does not. Read with TryGetProperty so a file
            // missing it reports the missing property rather than throwing out of the walk.
            int schemaVersion = ReadSchemaVersion(absolutePath, root);
            if (!BaselineFormat.IsSupported(schemaVersion))
                throw Malformed(
                    absolutePath,
                    $"unsupported schemaVersion {schemaVersion} "
                    + $"(expected {BaselineFormat.LegacySchemaVersion} or {BaselineFormat.SchemaVersion}).");

            bool sealsEntries = BaselineFormat.CarriesSeal(schemaVersion);
            if (sealsEntries)
                RequireExactProperties(absolutePath, root, "schemaVersion", "rules");
            else
                RequireExactProperties(absolutePath, root, "schemaVersion", "digest", "rules");

            string? legacyDigest = sealsEntries ? null : ReadDigest(absolutePath, root);

            using BaselineSealer? sealer = sealsEntries ? new BaselineSealer() : null;
            Dictionary<string, IReadOnlyList<BaselineEntry>> sections = ReadSections(absolutePath, root, schemaVersion, sealer);

            // A sealed file verified itself entry by entry on the way in. A legacy one has one digest over
            // all of them, so it is rebuilt here from the parsed entries in that version's frozen grammar:
            // formatting, order and CRLF changes are invisible; any entry change is not.
            if (legacyDigest is not null)
            {
                string recomputed = BaselineFormat.LegacyDigest(ToComposeInput(sections));
                if (!string.Equals(recomputed, legacyDigest, StringComparison.Ordinal))
                    throw LegacyDigestMismatch(absolutePath);
            }

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
        string path, JsonElement root, int schemaVersion, BaselineSealer? sealer)
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
                entries.Add(ReadEntry(path, ruleProperty.Name, entryElement, schemaVersion, sealer));
            sections[ruleProperty.Name] = entries;
        }

        return sections;
    }

    private static BaselineEntry ReadEntry(
        string path, string ruleId, JsonElement entry, int schemaVersion, BaselineSealer? sealer)
    {
        if (entry.ValueKind != JsonValueKind.Object) throw Malformed(path, $"rule '{ruleId}' has a non-object entry.");

        // One pass over the properties answers the whole shape question — {subject}, {source, target}, and
        // either of those plus a because, plus the two an edge's siteCount adds, plus the seal — and keeps
        // each value it meets, so nothing below walks the object a second time. This runs per entry, per
        // baseline file, on every check and status: materializing the names and set-comparing them once per
        // shape was work proportional to a team's whole debt ledger for a fixed question about six words.
        JsonElement? subject = null;
        JsonElement? source = null;
        JsonElement? target = null;
        JsonElement? because = null;
        JsonElement? siteCount = null;
        JsonElement? seal = null;
        var hasStranger = false;
        var propertyCount = 0;
        // A legacy file has no measure and no seal, so both are strangers there rather than optional keys:
        // a v1 file carrying either was hand-edited, and its digest was computed without them.
        bool measured = BaselineFormat.CarriesSiteCount(schemaVersion);
        bool sealsEntries = BaselineFormat.CarriesSeal(schemaVersion);

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
                case "seal" when sealsEntries:
                    seal = property.Value;
                    break;
                default:
                    hasStranger = true;
                    break;
            }
        }

        // Counting the distinct names back against the properties read is what rejects a repeated key: a
        // second 'subject' is a hand edit whose second value would silently never be read.
        int distinctNames = Present(subject) + Present(source) + Present(target) + Present(because)
                            + Present(siteCount) + Present(seal);
        bool exactlyNamed = !hasStranger && propertyCount == distinctNames;

        bool isSubject = exactlyNamed && subject is not null && source is null && target is null;
        bool isEdge = exactlyNamed && source is not null && target is not null && subject is null;
        if (!isSubject && !isEdge)
            throw Malformed(path, $"rule '{ruleId}' has an entry that is neither {{source, target}} nor {{subject}}.");

        if (isSubject && siteCount is not null)
            throw Malformed(path, $"rule '{ruleId}' has a 'siteCount' on a subject entry — only edge entries carry one.");

        // A seal is required where the version carries them, so its absence is a missing property rather
        // than tamper: the more precise diagnosis, and both refusals exit the same way.
        if (sealsEntries && seal is null) throw Malformed(path, $"rule '{ruleId}' has an entry with no 'seal'.");

        BaselineEntry parsed = isSubject
            ? BaselineEntry.ForSubject(RequireNonEmptyString(path, ruleId, "subject", subject!.Value))
            : BaselineEntry.ForEdge(
                RequireNonEmptyString(path, ruleId, "source", source!.Value),
                RequireNonEmptyString(path, ruleId, "target", target!.Value));

        if (siteCount is { } count)
            parsed = parsed.WithSiteCount(ReadSiteCount(path, ruleId, count));

        if (because is { } attribution)
            parsed = parsed.WithBecause(ReadBecause(path, ruleId, attribution));

        // Verified here rather than after the file is read, so the refusal names the entry that failed and
        // the rule it sits under while both are in hand.
        if (seal is { } stored)
        {
            string declared = ReadSeal(path, ruleId, stored);
            string computed = sealer!.Seal(ruleId, parsed);
            if (!string.Equals(computed, declared, StringComparison.Ordinal))
                throw SealMismatch(path, ruleId, parsed);
        }

        return parsed;
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
        bool blankOrMultiline = string.IsNullOrWhiteSpace(text) || SingleLineProse.IsMultiLine(text);
        if (blankOrMultiline) throw Malformed(path, $"rule '{ruleId}' has a blank or multi-line 'because'.");
        return text;
    }

    private static string ReadSeal(string path, string ruleId, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) throw Malformed(path, $"rule '{ruleId}' has a non-string 'seal'.");

        string seal = value.GetString()!;
        if (seal.Length != BaselineFormat.SealLength || !seal.All(IsLowerHex))
            throw Malformed(
                path, $"rule '{ruleId}' has a 'seal' that is not {BaselineFormat.SealLength} lowercase hex characters.");
        return seal;
    }

    private static int ReadSchemaVersion(string path, JsonElement root)
    {
        if (!root.TryGetProperty("schemaVersion", out JsonElement value))
            throw Malformed(path, "missing property 'schemaVersion'.");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int schemaVersion))
            throw Malformed(path, "schemaVersion must be an integer.");
        return schemaVersion;
    }

    // The whole-file digest is a legacy-version field: a current file carries none, its entries sealing
    // themselves one at a time.
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

    // The two integrity refusals read differently because the two formats fail differently, and the
    // difference is the recovery: one entry's line went wrong, or the one digest covering all of them did.
    private static UserErrorException SealMismatch(string path, string ruleId, BaselineEntry entry)
    {
        string identity = entry.Subject ?? $"{entry.Source} -> {entry.Target}";
        return new UserErrorException(
            $"Baseline file '{path}' failed its integrity check: the entry '{identity}' under rule '{ruleId}' " +
            "does not match its seal. " +
            "A baseline shrinks via 'loadbearing baseline --accept-reductions' and grows only via 'loadbearing baseline --add' " +
            "(one attributed entry at a time). If that entry was edited by hand, restore its line from version control. " +
            "If this file came out of a merge, resolve the conflict by keeping whole entry lines from either side rather " +
            "than editing one — every line carries its own seal.");
    }

    private static UserErrorException LegacyDigestMismatch(string path)
    {
        return new UserErrorException(
            $"Baseline file '{path}' failed its integrity check: the digest does not match the entries. " +
            "A baseline shrinks via 'loadbearing baseline --accept-reductions' and grows only via 'loadbearing baseline --add' " +
            "(one attributed entry at a time). If the file was edited by hand, restore it from version control. " +
            "If it came out of a merge, both committed sides carry a digest that is stale for the merged entries, so check " +
            "out one side's file and re-run 'loadbearing baseline --accept-reductions'.");
    }
}
