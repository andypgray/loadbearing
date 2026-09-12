using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The grandfathered-count checker the quoted-count gate runs on. A hand-written doc quotes captured
///     <c>check</c>, <c>status</c> and <c>baseline --init</c> output, and every ratcheted rule in those
///     captures carries a number: how many of its violations sit on the baseline beside them. This lifts
///     every such number out of a doc — from its fenced code blocks by shape, and from its prose by a
///     hand-registered template — and holds each to the <c>entries</c> array of the baseline JSON it came
///     from. All of the logic lives here so the gate and the unit tests exercise the same code path.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why <c>entries</c> is the right truth.</b> A report's grandfathered count is the number of
///         baseline entries that matched a violation and blessed it on that run; its stale count ("fixed
///         awaiting acceptance") is the number that matched nothing; and a matched entry whose pair now
///         carries more sites than it records is neither of those — it is <em>grown</em>, and red. The
///         identity is therefore <c>entries = grandfathered + stale + grown</c>.
///     </para>
///     <para>
///         <b>The two-term form still holds here.</b> This gate checks
///         <c>entries = grandfathered + stale</c>, which is that identity with a grown term of zero — and
///         zero is what every capture a doc can carry reports, because a run with anything grown is a
///         failing run. A doc quoting growth would be quoting a red <c>check</c> or a <c>status</c> whose
///         <c>new</c> count is not zero, and the captures under gate are all of green ones; quote a grown
///         run and the count no longer reconciles, which is a drift report rather than a silence. Two of
///         the five fenced shapes carry the stale count on the line, so for them the identity is checked
///         whole. The other three do not, and are pinned as <c>entries = grandfathered</c>: the same
///         identity with a stale term the quoted run reports as zero elsewhere in its own capture. Every
///         committed baseline here is at zero stale, and the day one is not, the capture it was quoted
///         from is stale too.
///     </para>
///     <para>
///         <b>A per-cell split is a partition of the row above it.</b> A family rule's <c>status</c> row
///         carries a keyed sub-line naming the cells its remaining debt sits in, and those counts sum to
///         the row's own remaining count. So the split is lifted as one number — its sum — and held to the
///         baseline the row above it in the same fence is held to. Two consequences worth stating: a split
///         that stops adding up to its row is a drift report rather than a silence, because only one of
///         the two can still agree with the committed entries; and a cell holding nothing is absent from
///         the line by design, which changes no sum.
///     </para>
///     <para>
///         <b>Prose is composed, not parsed.</b> A sentence saying "twelve inline-SQL references" is
///         registered as a template plus the rule ids that fill it, and the gate composes the expected
///         fragment from the baselines and asserts containment. Reword the sentence and the gate fails,
///         which is correct: the number has moved out of the shape the registry pins, and a registry
///         entry that matches nothing is a gate that holds nothing.
///     </para>
///     <para>
///         <b>Committed bytes only.</b> Like its sibling quote gates this reads the tracked file list and
///         the committed baselines, never the checker: no workspace load and no CLI invocation, so it
///         stays cheap, cross-platform and parallel-safe.
///     </para>
/// </remarks>
internal static class GrandfatheredCounts
{
    /// <summary>Whether a quoted grandfathered count still agrees with the baseline it came from.</summary>
    public enum CountBucket
    {
        /// <summary>The baseline holds exactly the quoted number of entries.</summary>
        Matched,

        /// <summary>No committed baseline answers for the quoted rule at all.</summary>
        NoBaseline,

        /// <summary>A baseline answers for the rule, and holds a different number of entries.</summary>
        Drifted
    }

    /// <summary>How a count is spelled where it is quoted.</summary>
    public enum CountStyle
    {
        /// <summary>Digits, as the tool prints them: <c>12</c>.</summary>
        Numeral,

        /// <summary>The number word in running prose: <c>twelve</c>.</summary>
        Word,

        /// <summary>The number word starting a sentence: <c>Thirty-three</c>.</summary>
        TitleWord
    }

    /// <summary>
    ///     The rule id standing for "every baseline under this root", which is what a <c>Burndown:</c>
    ///     line totals. It is not a legal rule id, so it can never collide with one.
    /// </summary>
    public const string RootTotal = "*";

    private const char EmDash = (char)0x2014;

    private const string BaselineDirectory = "arch/baselines";

    // How far from the word "grandfathered" a numeral or number word still reads as its count. Wide
    // enough for "the generated baseline carries four entries. Because they are grandfathered", which is
    // the furthest any prose in this repository puts them apart.
    private const int CountWindow = 8;

    private static readonly string[] Units =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen"
    ];

    private static readonly string[] Tens =
        ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    // Declared after Units and Tens because the initializer calls Spell, which reads them: a static field
    // initializer runs in declaration order, so hoisting this above them would leave the set built from
    // nulls.
    private static readonly HashSet<string> CountWords = Enumerable.Range(0, 100)
        .SelectMany(static count => new[]
        {
            Spell(count, CountStyle.Word),
            Spell(count, CountStyle.TitleWord),
            Spell(count, CountStyle.Numeral)
        })
        .ToHashSet(StringComparer.Ordinal);

    private const string RuleIdPattern = "[a-z][a-z0-9.-]*(?:/[a-z0-9-]+)+";

    // The site total the remaining term carries whenever its pairs cover more than one site each. Optional
    // because the line prints it only then, and ungrouped because sites are a different number against a
    // different truth: this gate reconciles entry counts, and an entry says nothing about how many sites it
    // covers until it has recorded one. Absent from the pattern entirely, a status line that grew it would
    // stop matching — and a shape this file no longer recognizes is not a red, it is a gate holding nothing.
    private const string SitesClause = @"(?: \(\d+ sites\))?";

    // The two measure states a write clears, each printed only when non-zero and neither moving the entry
    // count: an entry that came in under its recorded count, or records none, is matched and grandfathered
    // like any other. Optional for the reason above.
    private const string MeasureClauses = @"(?:, \d+ shrunk)?(?:, \d+ uncounted)?";

    // One cell of a per-cell split: its name, its remaining pairs, and the site total it prints only when
    // its pairs cover more than one site each — the row's own parenthetical, reused per cell and optional
    // for the same reason.
    private const string CellClause = @"[^,]+? (?<count>\d+)(?: \(\d+ sites\))?";

    /// <summary>
    ///     A <c>status</c> ratchet line: the rule, its posture, then the three counters. The third has two
    ///     spellings and a row reads exactly one of them — <c>not measured</c> where the rule's own selection
    ///     matched nothing, <c>fixed awaiting acceptance</c> where it was measured — so the alternation sits
    ///     inside the group and the identity below is unaffected by which it was: an entry no violation
    ///     matched is an entry no violation matched, and only what a reader should do about it differs.
    /// </summary>
    private static readonly Regex StatusLine =
        new($@"^\s*(?:pass|FAIL|warn|skip) (?<id>{RuleIdPattern}) \([a-z]+\) {EmDash} (?<count>\d+) grandfathered remaining{SitesClause}, \d+ new, (?<stale>\d+) (?:fixed awaiting acceptance|not measured){MeasureClauses}\s*$",
            RegexOptions.CultureInvariant);

    /// <summary>A <c>baseline --init</c> capture line, singular or plural.</summary>
    private static readonly Regex CapturedLine =
        new($@"^\s*(?<id>{RuleIdPattern}): captured (?<count>\d+) grandfathered violations?\.\s*$",
            RegexOptions.CultureInvariant);

    /// <summary>
    ///     The <c>Burndown:</c> tail of a <c>status</c> summary, which totals every baseline. Its uncounted
    ///     clause is the one that carries the nudge to record the counts, so it is spelled out here rather
    ///     than shared with <see cref="MeasureClauses" />.
    /// </summary>
    /// <remarks>
    ///     Unlike a row, the roll-up can carry <em>both</em> unmatched terms at once: it sums rules, and one
    ///     run can hold a rule that burned debt down beside a rule that rotted. So the second is its own
    ///     optional group rather than an alternation, and <see cref="Count" /> adds the two — the identity is
    ///     over every unmatched entry, however the line sorted them.
    /// </remarks>
    private static readonly Regex BurndownLine =
        new($@"\bBurndown: (?<count>\d+) grandfathered remaining{SitesClause}, (?<stale>\d+) fixed awaiting acceptance(?:, (?<unmeasured>\d+) not measured)?(?:, \d+ shrunk)?(?:, \d+ uncounted; run '[^']*' to record site counts)?\.",
            RegexOptions.CultureInvariant);

    /// <summary>
    ///     A grandfathered sub-line, in the human report (<c>grandfathered: 12 (baselined; …)</c>) or in
    ///     the <c>--json</c> baseline object (<c>"grandfathered": 4,</c>). Neither names its rule: the id
    ///     comes from the line above it in the same fence.
    /// </summary>
    private static readonly Regex SubLine =
        new(@"^\s*""?grandfathered""?:\s*(?<count>\d+)\b", RegexOptions.CultureInvariant);

    /// <summary>
    ///     The per-cell split under a family rule's <c>status</c> row: the cell word as a key, then a cell
    ///     name, its remaining pairs, and optionally the sites those cover. Like <see cref="SubLine" /> it
    ///     names no rule and takes the one declared above it in its own fence.
    /// </summary>
    /// <remarks>
    ///     The repeated group is what makes every cell's count a capture of <c>count</c>, so the whole line
    ///     is lifted as one sum without the pattern having to know how many cells a family has. A cell name
    ///     is anything up to the count that ends its clause — layer names are prose and may carry spaces —
    ///     which the lazy quantifier resolves in favour of the longest name and the last number.
    /// </remarks>
    private static readonly Regex CellsLine =
        new($@"^\s*(?:layer|project)s?: {CellClause}(?:, {CellClause})*\s*$", RegexOptions.CultureInvariant);

    /// <summary>A line that declares which rule the lines under it belong to, in a report or in JSON.</summary>
    private static readonly Regex RuleIdLine =
        new($@"^\s*(?:(?:pass|FAIL|warn|skip) (?<id>{RuleIdPattern})\b|""id"":\s*""(?<jsonId>{RuleIdPattern})"")",
            RegexOptions.CultureInvariant);

    /// <summary>
    ///     Extracts every grandfathered count from the fenced code blocks of <paramref name="docText" />.
    ///     Fence state comes from <see cref="SourceAnchors.FencedBlocks" />, which is also what keys a
    ///     bare <c>grandfathered:</c> sub-line — and a family row's per-cell split — to the rule declared
    ///     above it <em>in its own fence</em>: a rule id from the previous block must never answer for this
    ///     one. A sub-line with no rule above it in its fence is unattributable and is reported through
    ///     <see cref="Classify" />'s no-baseline bucket rather than silently dropped.
    /// </summary>
    public static IReadOnlyList<GrandfatheredCount> Extract(string doc, string docText)
    {
        List<GrandfatheredCount> counts = new();

        foreach (IReadOnlyList<(string Text, int Number)> block in SourceAnchors.FencedBlocks(docText))
        {
            var currentRuleId = string.Empty;
            foreach ((string line, int number) in block)
            {
                Match declaration = RuleIdLine.Match(line);
                if (declaration.Success) currentRuleId = FirstGroupValue(declaration, "id", "jsonId");

                Match status = StatusLine.Match(line);
                if (status.Success)
                {
                    counts.Add(Count(doc, number, status.Groups["id"].Value, status, hasStale: true));
                    continue;
                }

                Match cells = CellsLine.Match(line);
                if (cells.Success)
                {
                    counts.Add(Split(doc, number, currentRuleId, cells));
                    continue;
                }

                Match captured = CapturedLine.Match(line);
                if (captured.Success)
                {
                    counts.Add(Count(doc, number, captured.Groups["id"].Value, captured, hasStale: false));
                    continue;
                }

                Match burndown = BurndownLine.Match(line);
                if (burndown.Success)
                {
                    counts.Add(Count(doc, number, RootTotal, burndown, hasStale: true));
                    continue;
                }

                Match sub = SubLine.Match(line);
                if (sub.Success) counts.Add(Count(doc, number, currentRuleId, sub, hasStale: false));
            }
        }

        return counts;
    }

    /// <summary>
    ///     The repository-relative path of the baseline holding <paramref name="ruleId" />'s entries. The
    ///     rule id's slashes are real directories, so a scoped rule's baseline nests. An empty
    ///     <paramref name="exampleRoot" /> means this repository's own solution.
    /// </summary>
    public static string BaselinePath(string exampleRoot, string ruleId)
    {
        return $"{BaselineDirectoryOf(exampleRoot)}{ruleId}.json";
    }

    // The directory an example root's baselines live in, trailing slash included: the one place the
    // empty-root case is spelled, so a path, a prefix match and a failure message cannot disagree about it.
    private static string BaselineDirectoryOf(string exampleRoot)
    {
        return exampleRoot.Length == 0
            ? $"{BaselineDirectory}/"
            : $"{exampleRoot}/{BaselineDirectory}/";
    }

    /// <summary>
    ///     The tracked baseline files under <paramref name="exampleRoot" /> — the set a <c>Burndown:</c>
    ///     line totals. The prefix match is on the directory, so a sibling example whose path shares the
    ///     root as a string but not as a directory contributes nothing, and for this repository's own
    ///     root the fixture solutions' baselines stay out because their paths do not start at
    ///     <c>arch/baselines/</c>.
    /// </summary>
    public static IReadOnlyList<string> BaselineFiles(string exampleRoot, IEnumerable<string> trackedPaths)
    {
        string prefix = BaselineDirectoryOf(exampleRoot);

        return trackedPaths
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Where(static path => path.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    ///     The length of <paramref name="ruleId" />'s <c>entries</c> array in <paramref name="baselineText" />,
    ///     or <see langword="null" /> when the file holds no section for that rule. A rule id of
    ///     <see cref="RootTotal" /> totals every section in the file.
    /// </summary>
    public static int? EntryCount(string baselineText, string ruleId)
    {
        using JsonDocument document = JsonDocument.Parse(baselineText);
        if (!document.RootElement.TryGetProperty("rules", out JsonElement rules)) return null;

        if (ruleId == RootTotal)
        {
            var total = 0;
            foreach (JsonProperty rule in rules.EnumerateObject())
            {
                int? entries = EntryCountOf(rule.Value);
                if (entries is null) return null;

                total += entries.Value;
            }

            return total;
        }

        return rules.TryGetProperty(ruleId, out JsonElement section) ? EntryCountOf(section) : null;
    }

    /// <summary>
    ///     Decides whether <paramref name="count" /> still agrees with the baseline it was quoted from:
    ///     matched when the baseline holds exactly <c>count + stale</c> entries, no-baseline when nothing
    ///     committed answers for the rule, drifted when a baseline does and holds a different number.
    ///     <paramref name="trackedPaths" /> is only read for a <see cref="RootTotal" /> count, which sums
    ///     every baseline under the root.
    /// </summary>
    public static CountResult Classify(
        GrandfatheredCount count,
        string exampleRoot,
        IEnumerable<string> trackedPaths,
        Func<string, string?> readText)
    {
        if (count.RuleId.Length == 0)
            return new CountResult(
                CountBucket.NoBaseline,
                $"{count.Doc}:{count.DocLine} quotes a grandfathered count of {count.Count} with no rule declared above it in its fence.");

        Lookup lookup = Look(exampleRoot, count.RuleId, trackedPaths, readText);
        if (lookup.Total is null)
            return new CountResult(
                CountBucket.NoBaseline,
                $"{count.Doc}:{count.DocLine} quotes {Describe(count)}, but {lookup.Reason}.");

        int expected = count.Count + (count.Stale ?? 0);
        if (lookup.Total == expected) return new CountResult(CountBucket.Matched, null);

        return new CountResult(
            CountBucket.Drifted,
            $"{count.Doc}:{count.DocLine} quotes {Describe(count)}, but {lookup.Reason} holds {lookup.Total}.");
    }

    /// <summary>
    ///     The expected count for <paramref name="ruleId" /> — an <c>entries</c> length, or the total
    ///     across the root's baselines for <see cref="RootTotal" /> — or <see langword="null" /> when
    ///     nothing committed answers for it. This is what a registered prose template's placeholders are
    ///     filled from.
    /// </summary>
    public static int? Expected(
        string exampleRoot,
        string ruleId,
        IEnumerable<string> trackedPaths,
        Func<string, string?> readText)
    {
        return Look(exampleRoot, ruleId, trackedPaths, readText)
            .Total;
    }

    // The one baseline lookup both the fenced classifier and the prose composer resolve counts through:
    // a rule id reads its own file, RootTotal sums every baseline under the root, and every way of
    // finding nothing comes back as a null total with the reason already worded for a failure message.
    private static Lookup Look(
        string exampleRoot,
        string ruleId,
        IEnumerable<string> trackedPaths,
        Func<string, string?> readText)
    {
        string directory = BaselineDirectoryOf(exampleRoot);
        IReadOnlyList<string> paths = ruleId == RootTotal
            ? BaselineFiles(exampleRoot, trackedPaths)
            : [BaselinePath(exampleRoot, ruleId)];
        if (paths.Count == 0) return new Lookup(null, $"no baseline is committed under {directory}");

        var total = 0;
        foreach (string path in paths)
        {
            string? text = readText(path);
            if (text is null) return new Lookup(null, $"no baseline is committed at {path}");

            int? entries = TryEntryCount(text, ruleId);
            if (entries is null) return new Lookup(null, $"{path} holds no entries for it");

            total += entries.Value;
        }

        return new Lookup(total, string.Join(" + ", paths));
    }

    private static int? TryEntryCount(string baselineText, string ruleId)
    {
        try
        {
            return EntryCount(baselineText, ruleId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Lookup(int? Total, string Reason);

    /// <summary>
    ///     Spells <paramref name="count" /> the way <paramref name="style" /> writes it. Only 0 to 99 are
    ///     spellable as words; anything else throws, because a silently unspellable number would compose
    ///     a fragment that matches nothing and read as a passing gate.
    /// </summary>
    public static string Spell(int count, CountStyle style)
    {
        if (style == CountStyle.Numeral) return count.ToString(CultureInfo.InvariantCulture);

        if (count is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Only 0 to 99 are spelled as number words.");

        string word = count < Units.Length
            ? Units[count]
            : count % 10 == 0
                ? Tens[count / 10]
                : $"{Tens[count / 10]}-{Units[count % 10]}";

        return style == CountStyle.TitleWord ? char.ToUpperInvariant(word[0]) + word[1..] : word;
    }

    /// <summary>
    ///     Fills <paramref name="template" />'s <c>{0}</c>-style placeholders with
    ///     <paramref name="counts" /> spelled in <paramref name="style" /> — the fragment a registered
    ///     prose sentence must contain.
    /// </summary>
    public static string Compose(string template, IReadOnlyList<int> counts, CountStyle style)
    {
        object[] spelled = counts
            .Select(count => (object)Spell(count, style))
            .ToArray();
        return string.Format(CultureInfo.InvariantCulture, template, spelled);
    }

    /// <summary>
    ///     How a count reads where a gate scans prose for one, captured as <c>count</c>: a number word,
    ///     hyphenated above twenty, or a numeral. It sits beside <see cref="IsCountWord" /> because the two
    ///     must agree about what a count token looks like, and a copy of the fragment that stopped agreeing
    ///     would make its gate go quiet rather than red.
    /// </summary>
    internal const string CountToken = @"(?<count>[A-Za-z][a-z]*(-[a-z]+)?|\d+)";

    /// <summary>
    ///     Whether <paramref name="written" /> is a count as this repository's prose spells one: a number
    ///     word in running or sentence-initial case, or a numeral.
    /// </summary>
    /// <remarks>
    ///     The set is derived from <see cref="Spell" /> rather than written out a second time, which is
    ///     what keeps a claim a scanner accepts and a claim a gate can check from ever diverging. Every
    ///     gate that scans prose for a claim needs the test: a sentence like "Four new rules over this
    ///     repository's real code" puts a qualifier where the number goes, so requiring a spelled count
    ///     there is what tells a total from a delta.
    /// </remarks>
    public static bool IsCountWord(string written)
    {
        return CountWords.Contains(written);
    }

    /// <summary>
    ///     Collapses <paramref name="docText" />'s whitespace runs to single spaces, keeping the doc line
    ///     each surviving character came from. Prose wraps, so a registered fragment routinely spans a
    ///     line break; collapsing is what lets containment find it, and the retained line numbers are
    ///     what let a match still be reported — and claimed — by position.
    /// </summary>
    public static CollapsedText Collapse(string docText)
    {
        string normalized = docText.NormalizedLines();
        StringBuilder builder = new(normalized.Length);
        List<int> lines = new(normalized.Length);
        var line = 1;
        var pendingSpace = false;

        foreach (char character in normalized)
        {
            if (character == '\n')
            {
                line++;
                pendingSpace |= builder.Length > 0;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace |= builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                lines.Add(line);
                pendingSpace = false;
            }

            builder.Append(character);
            lines.Add(line);
        }

        return new CollapsedText(builder.ToString(), lines);
    }

    /// <summary>
    ///     Every line of <paramref name="docText" /> outside a fenced code block that mentions
    ///     grandfathering with a count near it — the sweep that finds a prose site no registry entry
    ///     covers. "Near" is <see cref="CountWindow" /> whitespace-separated tokens either side of the
    ///     word, on the same line: a number further away than that is discussing something else.
    /// </summary>
    public static IReadOnlyList<ProseMention> ProseMentions(string doc, string docText)
    {
        List<ProseMention> mentions = new();

        foreach ((string text, int number) in SourceAnchors.UnfencedLines(docText))
            if (HasCountNearGrandfathered(text))
                mentions.Add(new ProseMention(doc, number, text));

        return mentions;
    }

    // A per-cell split lifted as the one number it partitions: every cell's count summed, carrying no stale
    // term of its own, because the split says where the row's remaining debt sits and the row beside it is
    // where the stale count is quoted.
    private static GrandfatheredCount Split(string doc, int number, string ruleId, Match match)
    {
        int total = match.Groups["count"]
            .Captures.Sum(capture => int.Parse(capture.Value));
        return new GrandfatheredCount(doc, number, ruleId, total, null);
    }

    private static GrandfatheredCount Count(string doc, int number, string ruleId, Match match, bool hasStale)
    {
        int count = int.Parse(match.Groups["count"].Value);
        // Both unmatched terms, because the identity is over every entry no violation matched and only the
        // Burndown line splits them. A pattern that never names the second leaves a group that did not
        // participate, so this reads as zero there rather than needing a flag of its own.
        Group unmeasured = match.Groups["unmeasured"];
        int? stale = hasStale
            ? int.Parse(match.Groups["stale"].Value) + (unmeasured.Success ? int.Parse(unmeasured.Value) : 0)
            : null;
        return new GrandfatheredCount(doc, number, ruleId, count, stale);
    }

    private static string FirstGroupValue(Match match, params string[] names)
    {
        foreach (string name in names)
            if (match.Groups[name]
                .Success)
                return match.Groups[name]
                    .Value;

        return string.Empty;
    }

    private static int? EntryCountOf(JsonElement section)
    {
        return section.TryGetProperty("entries", out JsonElement entries) && entries.ValueKind == JsonValueKind.Array
            ? entries.GetArrayLength()
            : null;
    }

    private static string Describe(GrandfatheredCount count)
    {
        string subject = count.RuleId == RootTotal ? "a burndown total" : $"'{count.RuleId}'";
        string stale = count.Stale is null ? string.Empty : $" and {count.Stale} fixed awaiting acceptance";
        return $"{subject} as {count.Count} grandfathered{stale}";
    }

    private static bool HasCountNearGrandfathered(string line)
    {
        string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < tokens.Length; index++)
        {
            if (!tokens[index]
                    .Contains("grandfather", StringComparison.OrdinalIgnoreCase)) continue;

            int from = Math.Max(0, index - CountWindow);
            int to = Math.Min(tokens.Length - 1, index + CountWindow);
            for (int near = from; near <= to; near++)
                if (near != index && IsCountToken(tokens[near]))
                    return true;
        }

        return false;
    }

    private static bool IsCountToken(string token)
    {
        string bare = token.Trim('.', ',', ';', ':', '(', ')', '[', ']', '`', '*', '_', '"', '\'', '!', '?');
        if (bare.Length == 0) return false;

        if (bare.All(char.IsAsciiDigit)) return true;

        return Units.Contains(bare, StringComparer.OrdinalIgnoreCase)
               || Tens.Any(ten => ten.Length > 0 && bare.StartsWith(ten, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     The outcome of classifying one quoted count: the bucket it fell in, and — when that bucket is
    ///     not <see cref="CountBucket.Matched" /> — the human-readable failure describing the
    ///     disagreement.
    /// </summary>
    public sealed record CountResult(CountBucket Bucket, string? Failure);

    /// <summary>
    ///     One unfenced line that mentions grandfathering with a count near it: a candidate prose site the
    ///     registry must either cover or exempt.
    /// </summary>
    public sealed record ProseMention(string Doc, int DocLine, string Text);

    /// <summary>
    ///     A doc's text with its whitespace collapsed, keeping the doc line each character came from so a
    ///     fragment found by containment can still be located by line.
    /// </summary>
    public sealed class CollapsedText(string text, IReadOnlyList<int> lines)
    {
        /// <summary>The collapsed text: no line breaks, every whitespace run one space.</summary>
        public string Text { get; } = text;

        /// <summary>The 1-based doc line the character at <paramref name="index" /> came from.</summary>
        public int LineAt(int index)
        {
            return lines[index];
        }

        /// <summary>
        ///     The inclusive doc line range spanned by <paramref name="fragment" />, or
        ///     <see langword="null" /> when the collapsed text does not contain it.
        /// </summary>
        public (int First, int Last)? Span(string fragment)
        {
            int start = Text.IndexOf(fragment, StringComparison.Ordinal);
            if (start < 0 || fragment.Length == 0) return null;

            return (LineAt(start), LineAt(start + fragment.Length - 1));
        }
    }
}
