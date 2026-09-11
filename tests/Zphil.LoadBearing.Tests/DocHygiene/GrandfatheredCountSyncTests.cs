using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The quote-sync gate over quoted grandfathered counts. The example walkthroughs and the root README
///     quote captured <c>check</c>, <c>status</c> and <c>baseline --init</c> output, and every ratcheted
///     rule in those captures carries the size of its baseline — in the fenced capture, and again in the
///     prose that recaps it. Nothing regenerates a hand-written doc, so this gate holds each number to the
///     <c>entries</c> array of the committed baseline it came from: a re-baseline that leaves a
///     walkthrough behind fails the suite instead of publishing a page that contradicts the JSON beside
///     it.
/// </summary>
/// <remarks>
///     Sibling to <see cref="RuleQuoteSyncTests" />, which holds the rendered sentence in the same
///     captures against the <c>AGENTS.md</c> beside them, and to <see cref="WrittenPathSyncTests" />,
///     which holds their file lists against the tracked set. The three cover the three things a captured
///     report says: what the rule is, what it touched, and how much of it is on the record. Like them
///     this reads only committed bytes: no workspace load, no CLI invocation, so it stays cheap and
///     parallel-safe.
/// </remarks>
public sealed class GrandfatheredCountSyncTests
{
    private const string RootReadme = "README.md";
    private const string MeridianReadme = "examples/Meridian/README.md";
    private const string MeridianAdopting = "examples/Meridian/ADOPTING.md";
    private const string MeridianStoryboard = "examples/Meridian/hooks/storyboard.md";
    private const string InterchangeReadme = "examples/Meridian.Interchange/README.md";
    private const string OperationsReadme = "examples/Meridian.Operations/README.md";
    private const string QuotingReadme = "examples/Meridian.Quoting/README.md";
    private const string DeriveSpecPrompt = "src/Zphil.LoadBearing.Cli/Mcp/Prompts/derive-spec.md";

    private const string MeridianRoot = "examples/Meridian";
    private const string InterchangeRoot = "examples/Meridian.Interchange";
    private const string OperationsRoot = "examples/Meridian.Operations";

    /// <summary>
    ///     This repository's own root, the baseline base for docs quoting a check over this solution
    ///     rather than over an example. Empty because its baselines are the ones at <c>arch/baselines/</c>.
    /// </summary>
    private const string SelfRoot = "";

    private const string InlineSql = "data-access/no-inline-sql";
    private const string InjectClock = "time/inject-clock";
    private const string AsyncSuffix = "naming/async-suffix";
    private const string ClearanceContainment = "clearance/engine/containment";
    private const string DemurrageContainment = "demurrage/engine/containment";
    private const string NoSyncOverAsync = "async/no-sync-over-async";
    private const string EnvThroughSeam = "mcp/env-through-seam";

    /// <summary>The count-quoting docs, each paired with the root whose baselines it quotes.</summary>
    private static readonly (string Doc, string ExampleRoot)[] CountDocs =
    [
        (MeridianReadme, MeridianRoot),
        (MeridianAdopting, MeridianRoot),
        (MeridianStoryboard, MeridianRoot),
        (InterchangeReadme, InterchangeRoot),
        (OperationsReadme, OperationsRoot),
        // The root README's dogfood spine quotes a self-check over this solution, so its counts come from
        // this repository's own baselines.
        (RootReadme, SelfRoot)
    ];

    private static readonly DocGate<GrandfatheredCount> Gate = new(
        CountDocs.Select(static entry => entry.Doc)
            .ToArray(),
        ExtractDoc,
        quotes: "grandfathered counts");

    /// <summary>
    ///     The prose recaps of those counts. A sentence is registered as the fragment it is written in
    ///     with the counts left as placeholders, plus the rules that fill them; the gate composes the
    ///     expected text from the committed baselines and asserts the doc contains it. Composition rather
    ///     than parsing is what makes a reworded sentence a failure — the number has moved out of the
    ///     shape this entry pins, and an entry that matches nothing holds nothing.
    /// </summary>
    private static readonly ProseCount[] ProseCounts =
    [
        new(MeridianReadme, MeridianRoot,
            "{0} current violations are grandfathered",
            [GrandfatheredCounts.RootTotal],
            GrandfatheredCounts.CountStyle.TitleWord),
        new(MeridianReadme, MeridianRoot,
            "{0} inline-SQL references, {1} wall-clock reads, {2} `Task`-returning methods missing the `Async` suffix, and {3} reach into the quarantined module",
            [InlineSql, InjectClock, AsyncSuffix, ClearanceContainment],
            GrandfatheredCounts.CountStyle.Word),
        new(MeridianReadme, MeridianRoot,
            "the {0} grandfathered ones stay quiet",
            [InlineSql],
            GrandfatheredCounts.CountStyle.Word),
        new(MeridianReadme, MeridianRoot,
            "{0} reach into the module already exists",
            [ClearanceContainment],
            GrandfatheredCounts.CountStyle.TitleWord),
        new(MeridianAdopting, MeridianRoot,
            // Step 5's refinement, and the one place the walkthrough states the curated rule's own count.
            "which leaves {0} grandfathered controller reads and one type doing its job",
            [InjectClock],
            GrandfatheredCounts.CountStyle.Word),
        new(MeridianAdopting, MeridianRoot,
            "holding {0} inline-SQL references, {1} clock reads, {2} unsuffixed methods, and {3} inbound reach",
            [InlineSql, InjectClock, AsyncSuffix, ClearanceContainment],
            GrandfatheredCounts.CountStyle.Word),
        new(MeridianAdopting, MeridianRoot,
            "prints its grandfathered count ({0}, {1}, {2}, and {3})",
            [InlineSql, InjectClock, AsyncSuffix, ClearanceContainment],
            GrandfatheredCounts.CountStyle.Numeral),
        new(MeridianStoryboard, MeridianRoot,
            "The {0} inline-SQL sites already on the record stay quiet",
            [InlineSql],
            GrandfatheredCounts.CountStyle.Word),
        new(MeridianStoryboard, MeridianRoot,
            "with `grandfathered: {0}`",
            [InlineSql],
            GrandfatheredCounts.CountStyle.Numeral),
        new(MeridianStoryboard, MeridianRoot,
            "its {0} grandfathered sites stay quiet",
            [AsyncSuffix],
            GrandfatheredCounts.CountStyle.Word),
        new(MeridianStoryboard, MeridianRoot,
            "is one of those {0}",
            [AsyncSuffix],
            GrandfatheredCounts.CountStyle.Word),
        new(InterchangeReadme, InterchangeRoot,
            "the generated baseline carries {0} entries",
            [NoSyncOverAsync],
            GrandfatheredCounts.CountStyle.Word),
        new(OperationsReadme, OperationsRoot,
            // The closing line of the quarantine argument: the count of grandfathered edges is the point.
            "{0} edge, one owner",
            [DemurrageContainment],
            GrandfatheredCounts.CountStyle.TitleWord),
        new(RootReadme, SelfRoot,
            // The SARIF section's own claim about this repository's one ratcheted rule. It said "four"
            // for two days after the baseline shrank to two, which is the drift this gate now catches.
            "Its {0} baselined sites keep the rule green",
            [EnvThroughSeam],
            GrandfatheredCounts.CountStyle.Word)
    ];

    /// <summary>
    ///     Draft-era counts: sentences that deliberately state a number the committed baseline does not
    ///     hold, because the walkthrough is mid-authoring at that point. Each is pinned from both sides —
    ///     the drafted text must still be there, and the baseline's number must still be absent — so the
    ///     day the divergence closes, the exemption fails rather than quietly covering a real drift.
    /// </summary>
    private static readonly DraftCount[] DraftCounts =
    [
        // ADOPTING.md step 4 runs the blunt draft rule, before step 5 adds `.Except(SystemClock)`. The
        // draft flags the sanctioned seam alongside the controller reads, so its evidence is one higher
        // than the curated rule's baseline; step 5 walks through exactly that subtraction.
        new(MeridianAdopting, MeridianRoot,
            "`time/inject-clock` reports eight sites",
            "`time/inject-clock` reports {0} sites",
            InjectClock,
            GrandfatheredCounts.CountStyle.Word),
        new(MeridianAdopting, MeridianRoot,
            "| `time/inject-clock` | 8 clock reads |",
            "| `time/inject-clock` | {0} clock reads |",
            InjectClock,
            GrandfatheredCounts.CountStyle.Numeral),
        new(MeridianAdopting, MeridianRoot,
            "The clock rule has eight sites",
            "The clock rule has {0} sites",
            InjectClock,
            GrandfatheredCounts.CountStyle.Word)
    ];

    /// <summary>
    ///     Prose the sweep flags that quotes no count: the word sits near a number that means something
    ///     else. Each entry must still match a swept line, so an exemption cannot outlive the sentence it
    ///     was written for.
    /// </summary>
    private static readonly (string Doc, string Marker, string Reason)[] SweepExemptions =
    [
        (DeriveSpecPrompt,
            "grandfathered in step 7)",
            "The 7 is the recipe's step number, not a count of anything baselined."),
        (DeriveSpecPrompt,
            "with the grandfathered counts visible",
            "The 0 is the expected `rulesFailed` of the re-check, not a grandfathered count."),
        (MeridianReadme,
            "Three describe debt with a target",
            "The counts are of rules by posture — three law, three ratcheting, one quarantine — not of "
            + "the violations any of them grandfathers."),
        (MeridianReadme,
            "Move a controller onto a repository and its grandfathered count drops",
            "The zero is where a burndown ends, not a count the baseline holds today."),
        (InterchangeReadme,
            "grandfathers the one legacy corner on a counted baseline",
            "The one is the legacy corner the rule ratchets — a single adapter type — while its baseline "
            + "carries four entries, one per blocking member it touches."),
        (QuotingReadme,
            "with no baseline to grandfather",
            "The two counts rules matching by name across two specs, in a subsystem that baselines nothing.")
    ];

    [Fact]
    public void QuotedGrandfatheredCounts_MatchTheirBaseline()
    {
        // Arrange
        Func<string, string?> read = RuleQuotes.DiskTextReader(RepoRoot.Directory);
        List<string> drift = new();

        // Act: every fenced count, resolved against its own root's baselines, must equal the entries the
        // run it was captured from would have matched.
        foreach ((string doc, string exampleRoot) in CountDocs)
        foreach (GrandfatheredCount count in Gate.Scan(doc))
        {
            GrandfatheredCounts.CountResult result =
                GrandfatheredCounts.Classify(count, exampleRoot, TrackedFiles.All, read);
            if (result.Bucket != GrandfatheredCounts.CountBucket.Matched) drift.Add(result.Failure!);
        }

        // Assert
        drift.ShouldReportNothing("Quoted grandfathered counts no longer match the baselines they came from");
    }

    [Fact]
    public void EveryCountDoc_YieldsAtLeastOneCount()
    {
        // Act & Assert: guard against the scanner silently matching nothing if a doc's quoting style changes.
        Gate.ShouldYieldFromEveryRegisteredDoc();
    }

    [Fact]
    public void EveryTrackedDocQuotingFencedCounts_IsRegistered()
    {
        // Act & Assert: the registry above is hand-written, so the failure it cannot see is a doc that
        // quotes a count and was never added to it — a whole walkthrough silently outside the gate.
        Gate.ShouldFindNothingOutsideTheRegistry(
            counted: "grandfathered count(s)",
            swept: "grandfathered counts",
            authority: "a baseline");
    }

    [Fact]
    public void ProseCounts_ReadAsTheirBaselinesComposeThem()
    {
        // Arrange
        List<string> drift = new();

        // Act: compose each registered sentence from the committed baselines and require the doc to carry
        // it. Whitespace is collapsed first, because prose wraps and these fragments span line breaks.
        foreach (ProseCount prose in ProseCounts)
        {
            string? fragment = Compose(prose);
            if (fragment is null)
            {
                drift.Add($"{prose.Doc}: no committed baseline answers for [{string.Join(", ", prose.RuleIds)}] under {prose.ExampleRoot}.");
                continue;
            }

            if (CollapsedDoc(prose.Doc)
                    .Span(fragment) is null)
                drift.Add($"{prose.Doc} no longer reads '{fragment}', which its baselines compose from template '{prose.Template}'.");
        }

        // Assert
        drift.ShouldReportNothing("Prose recaps of grandfathered counts no longer match the baselines they came from");
    }

    [Fact]
    public void EveryProseCountMention_IsCoveredOrExempt()
    {
        // Arrange: the fenced sweep cannot see prose, so this is its counterpart — every unfenced mention
        // of grandfathering with a number beside it must be claimed by a registered sentence or carry a
        // written reason for being left out.
        List<string> uncovered = new();

        // Act
        foreach (string path in TrackedFiles.Markdown)
        {
            IReadOnlySet<int> claimed = ClaimedLines(path);
            foreach (GrandfatheredCounts.ProseMention mention in SweepDoc(path))
            {
                if (claimed.Contains(mention.DocLine) || IsExempt(mention)) continue;

                uncovered.Add($"{mention.Doc}:{mention.DocLine} states a grandfathered count no registry entry covers: {mention.Text.Trim()}");
            }
        }

        // Assert
        uncovered.ShouldReportNothing("These prose sites state a grandfathered count that nothing holds to a baseline");
    }

    [Fact]
    public void SweepExemptions_StillMatchASweptMention()
    {
        // Arrange
        GrandfatheredCounts.ProseMention[] swept = TrackedFiles.Markdown
            .SelectMany(SweepDoc)
            .ToArray();
        List<string> dead = new();

        // Act: an exemption whose sentence has been rewritten is no longer excusing anything, and leaving
        // it in place would hide the day that line starts quoting a real count.
        foreach ((string doc, string marker, string _) in SweepExemptions)
            if (!swept.Any(mention => mention.Doc == doc && mention.Text.Contains(marker, StringComparison.Ordinal)))
                dead.Add($"exemption {doc} -> '{marker}' matches no swept line.");

        // Assert
        dead.ShouldReportNothing("These sweep exemptions no longer correspond to any prose and should be removed");
    }

    [Fact]
    public void DraftCounts_StillDivergeFromTheirBaseline()
    {
        // Arrange
        List<string> failures = new();

        // Act: both halves of the pin. The drafted sentence must still be in the doc, and the baseline's
        // own number must still be absent from it — the day they agree, the reason this text is exempt has
        // gone away and it should join the registry above.
        foreach (DraftCount draft in DraftCounts)
        {
            GrandfatheredCounts.CollapsedText collapsed = CollapsedDoc(draft.Doc);
            if (collapsed.Span(draft.Fragment) is null) failures.Add($"{draft.Doc} no longer reads the drafted '{draft.Fragment}'.");

            int? expected = Expected(draft.ExampleRoot, draft.RuleId);
            if (expected is null)
            {
                failures.Add($"{draft.Doc}: no committed baseline answers for '{draft.RuleId}' under {draft.ExampleRoot}.");
                continue;
            }

            string composed = GrandfatheredCounts.Compose(draft.Template, [expected.Value], draft.Style);
            if (collapsed.Span(composed) is not null)
                failures.Add($"{draft.Doc} now reads '{composed}', which its baseline composes; the draft exemption is dead and the sentence should be registered.");
        }

        // Assert
        failures.ShouldReportNothing("Draft-era counts no longer diverge from the baselines they precede");
    }

    private static IReadOnlyList<GrandfatheredCount> ExtractDoc(string doc)
    {
        return GrandfatheredCounts.Extract(doc, RepoRoot.ReadText(doc));
    }

    private static IReadOnlyList<GrandfatheredCounts.ProseMention> SweepDoc(string doc)
    {
        return GrandfatheredCounts.ProseMentions(doc, RepoRoot.ReadText(doc));
    }

    private static GrandfatheredCounts.CollapsedText CollapsedDoc(string doc)
    {
        return GrandfatheredCounts.Collapse(RepoRoot.ReadText(doc));
    }

    private static int? Expected(string exampleRoot, string ruleId)
    {
        return GrandfatheredCounts.Expected(
            exampleRoot,
            ruleId,
            TrackedFiles.All,
            RuleQuotes.DiskTextReader(RepoRoot.Directory));
    }

    private static string? Compose(ProseCount prose)
    {
        int?[] counts = prose.RuleIds
            .Select(ruleId => Expected(prose.ExampleRoot, ruleId))
            .ToArray();
        if (counts.Any(static count => count is null)) return null;

        return GrandfatheredCounts.Compose(
            prose.Template,
            counts.Select(static count => count!.Value)
                .ToArray(),
            prose.Style);
    }

    // The doc lines a registered prose sentence occupies — what makes the sweep's "covered" question
    // answerable by position, and what stops a second uncovered sentence hiding behind a first.
    private static IReadOnlySet<int> ClaimedLines(string doc)
    {
        GrandfatheredCounts.CollapsedText collapsed = CollapsedDoc(doc);
        HashSet<int> claimed = new();

        foreach (ProseCount prose in ProseCounts.Where(entry => entry.Doc == doc))
        {
            string? fragment = Compose(prose);
            if (fragment is null) continue;

            (int First, int Last)? span = collapsed.Span(fragment);
            if (span is null) continue;

            for (int line = span.Value.First; line <= span.Value.Last; line++) claimed.Add(line);
        }

        return claimed;
    }

    private static bool IsExempt(GrandfatheredCounts.ProseMention mention)
    {
        return SweepExemptions.Any(exemption =>
            exemption.Doc == mention.Doc && mention.Text.Contains(exemption.Marker, StringComparison.Ordinal));
    }

    /// <summary>
    ///     One registered prose recap: the <paramref name="Doc" /> it lives in, the
    ///     <paramref name="ExampleRoot" /> whose baselines answer for it, the <paramref name="Template" />
    ///     it is written in with <c>{0}</c>-style placeholders for its counts, the
    ///     <paramref name="RuleIds" /> that fill them in order, and the <paramref name="Style" /> the
    ///     sentence spells them in.
    /// </summary>
    private sealed record ProseCount(
        string Doc,
        string ExampleRoot,
        string Template,
        IReadOnlyList<string> RuleIds,
        GrandfatheredCounts.CountStyle Style);

    /// <summary>
    ///     One draft-era count: the <paramref name="Fragment" /> the doc must still carry, and the
    ///     <paramref name="Template" /> its <paramref name="RuleId" />'s baseline must still <em>not</em>
    ///     compose into it.
    /// </summary>
    private sealed record DraftCount(
        string Doc,
        string ExampleRoot,
        string Fragment,
        string Template,
        string RuleId,
        GrandfatheredCounts.CountStyle Style);
}
