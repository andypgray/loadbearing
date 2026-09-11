using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The quote-sync gate over the hand-written docs' fenced excerpts. The landing page and the example
///     walkthroughs quote committed files inside fenced code blocks — a starter rule from the quoting
///     example's spec, two rules of the self-spec, the rendered <c>AGENTS.md</c> bullets a spec generates,
///     the adapter class that runs them all as tests, the rendered Mermaid diagram of this solution, the
///     spec project's csproj — and this gate holds each quote to the file it was cut from: every non-blank
///     line of the excerpt's fence must appear, in order, as a verbatim substring of its source, so an
///     edit to the source that the doc does not follow fails the suite instead of publishing a stale
///     quote. Five excerpts are captured tool output with no committed source; they are
///     demonstration-exempt, held only to the requirement that their fences still exist, so an exemption
///     cannot silently go dead.
/// </summary>
/// <remarks>
///     This gate proves doc → committed generated file, and stops there. That the committed generated
///     file still matches what the renderer emits is proved separately — by the dogfood self-spec tests
///     and by CI re-rendering the examples and failing on any diff — because the render comparison is
///     expensive and serialized, and this gate reads only committed bytes so it stays cheap and
///     parallel-safe. <see cref="RuleQuoteSyncTests" /> covers the other quote shape: a rule-header line
///     quoted from captured <c>check</c> output, whose rendered sentence sits mid-bullet rather than on a
///     line of its own.
/// </remarks>
public sealed class QuoteSyncTests
{
    private const string RootReadme = "README.md";
    private const string MeridianReadme = "examples/Meridian/README.md";
    private const string MeridianAdopting = "examples/Meridian/ADOPTING.md";
    private const string QuotingReadme = "examples/Meridian.Quoting/README.md";
    private const string OperationsReadme = "examples/Meridian.Operations/README.md";
    private const string InterchangeReadme = "examples/Meridian.Interchange/README.md";

    private const string SelfSpec = "arch/Zphil.LoadBearing.ArchSpec/LoadBearingArchSpec.cs";

    /// <summary>
    ///     The registered quoted excerpts, each keyed by the doc it lives in and by a distinctive marker
    ///     substring that must pick out exactly one of that doc's fences, and paired with the committed
    ///     source the fence is synced against. An excerpt with a null source is demonstration-exempt:
    ///     nothing is synced against it, but its fence must still exist.
    /// </summary>
    private static readonly Excerpt[] Excerpts =
    [
        new(
            // The landing example: quoted dedented from the example spec, which the substring check
            // permits because a dedented line is still contained in its indented source line.
            RootReadme,
            "starter-rule",
            "arch.Rule(\"layering/domain-independent\")",
            "examples/Meridian.Quoting/arch/Meridian.Quoting.ArchSpec/QuotingArchSpec.cs"),
        new(
            RootReadme,
            "self-spec-rule",
            "arch.Rule(\"cli/no-stdout\")",
            SelfSpec),
        new(
            // The rendered bullet carries the derived rule sentence and the rule's Because on one line,
            // which the FAIL stanza below splits across two; that join is what makes this marker pick out
            // the AGENTS.md fence rather than the hook report.
            RootReadme,
            "agents-block",
            "`Console.WriteLine()`. Stdout is a protocol channel",
            "AGENTS.md"),
        new(
            RootReadme,
            "adapter-test",
            "class AdapterSelfSpecTests : ArchRuleTests<LoadBearingArchSpec>",
            "tests/Zphil.LoadBearing.Tests/Dogfood/AdapterSelfSpecTests.cs"),
        new(
            // The one Migrate fence on the page. It is cut from Meridian rather than from the self-spec:
            // this repository's own ratchet reached zero and was promoted, and a ratchet needs live debt
            // to show.
            RootReadme,
            "migrate-rule",
            "arch.Rule(\"data-access/no-inline-sql\")",
            "examples/Meridian/arch/Meridian.ArchSpec/MeridianArchSpec.cs"),
        new(
            // The one tool-output fence with a committed counterpart: `render --diagram` writes the block
            // into ARCHITECTURE.md, so the README quote is synced rather than demonstration-exempt. The
            // marker is the accessible title rather than `flowchart LR`, which stopped being distinctive
            // the moment that one block grew a second fence.
            RootReadme,
            "graph-diagram",
            "accTitle: Codebase survey:",
            "ARCHITECTURE.md"),
        new(
            // The law fence out of the same block, drawn from the spec rather than from the codebase.
            RootReadme,
            "law-diagram",
            "accTitle: Architecture law:",
            "ARCHITECTURE.md"),

        // The five entries below quote output captured from a run rather than a committed file, so there
        // is nothing to sync them against and they are demonstration-exempt — the exactly-one-fence guard
        // still holds each fence in place. hook-report is the stderr a red self-check feeds an agent
        // through the wrapper in hooks/; sarif-result is one result object from `check --sarif`;
        // check-json is that same rule's entry from `check --json` (whose `"id":` spelling is what keeps
        // the sarif-result `"ruleId":` marker distinctive); graph-survey is a slice of the `graph`
        // project roster; legacy-check is a stanza from a check over the non-SDK-style ClassicApp test
        // fixture, whose lines ClassicProjectCheckTests pins. If a committed capture of any of them ever
        // lands, give that entry its source path and it graduates to a synced excerpt.
        new(
            RootReadme,
            "hook-report",
            "FAIL cli/no-stdout",
            null),
        new(
            RootReadme,
            "sarif-result",
            "\"ruleId\": \"data-access/no-inline-sql\"",
            null),
        new(
            RootReadme,
            "check-json",
            "\"posture\": \"migrate\"",
            null),
        new(
            RootReadme,
            "graph-survey",
            "192 types; references: (none)",
            null),
        new(
            RootReadme,
            "legacy-check",
            "Classic.Billing/BillingCalculator.cs:10",
            null),

        // The example walkthroughs quote the AGENTS.md their own spec renders — the root managed block
        // for a subsystem, or a whole scoped card dropped beside a module's code, markers and all. Each is
        // synced against the committed file `render` writes, so a spec edit that re-renders the block
        // without the walkthrough following it fails here. The markers are structural — a heading or the
        // card's dragons line — because what must stay distinctive is which fence, not which wording.
        new(
            MeridianReadme,
            "meridian-agents-block",
            "### Quarantined scopes",
            "examples/Meridian/AGENTS.md"),
        new(
            // The quarantined module's card, quoted without its markers or its generated-by line: the
            // in-order substring check reads straight past the source lines the walkthrough leaves out.
            MeridianReadme,
            "meridian-dragons-card",
            "Dragons: ISO 6346 check digit:",
            "examples/Meridian/src/Meridian.Clearance/AGENTS.md"),
        new(
            QuotingReadme,
            "quoting-agents-block",
            "`time/injected-clock`",
            "examples/Meridian.Quoting/AGENTS.md"),
        new(
            OperationsReadme,
            "operations-invoicing-card",
            "## Layer `Invoicing`",
            "examples/Meridian.Operations/src/Meridian.Operations/Invoicing/AGENTS.md"),
        new(
            OperationsReadme,
            "operations-demurrage-card",
            "## Quarantined scope `demurrage/engine`",
            "examples/Meridian.Operations/src/Meridian.Operations/Demurrage/AGENTS.md"),
        new(
            InterchangeReadme,
            "interchange-agents-block",
            "- `di/hosted-services-scope-their-work`",
            "examples/Meridian.Interchange/AGENTS.md"),
        new(
            // Not a rendered block: the adoption walkthrough shows the spec project "exactly as
            // committed", which is a promise the same in-order substring check can keep.
            MeridianAdopting,
            "adopting-spec-csproj",
            "<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>",
            "examples/Meridian/arch/Meridian.ArchSpec/Meridian.ArchSpec.csproj")
    ];

    /// <summary>
    ///     The registered docs and their fences. Unlike its siblings this gate scans fences rather than a
    ///     quote type — its unit of registration is a marker inside a doc, not the doc — so it takes the
    ///     harness for the emptiness guard and the one scan per doc, and states no registry sweep.
    /// </summary>
    private static readonly DocGate<IReadOnlyList<string>> Gate = new(
        Excerpts.Select(static excerpt => excerpt.Doc)
            .Distinct(StringComparer.Ordinal)
            .ToArray(),
        static doc => SourceAnchors.Fences(RepoRoot.ReadText(doc)),
        quotes: "fenced code blocks",
        scanner: "fence scanner");

    [Fact]
    public void ExcerptRegistry_IsNotEmpty()
    {
        // Act & Assert: every other test in this class iterates the registry, so an empty one would make
        // all of them pass over nothing.
        Excerpts.ShouldNotBeEmpty("The excerpt registry is empty; every quote-sync assertion below would pass vacuously.");
    }

    [Fact]
    public void EachExcerpt_MatchesExactlyOneFence()
    {
        // Arrange
        List<string> failures = new();

        // Act: every excerpt's marker — including the demonstration-exempt one — must pick out exactly one
        // fence of its own doc; zero means the excerpt was dropped or reworded, more than one means it is
        // no longer distinctive.
        foreach (IGrouping<string, Excerpt> group in Excerpts.GroupBy(static excerpt => excerpt.Doc))
        {
            IReadOnlyList<IReadOnlyList<string>> fences = Gate.Scan(group.Key);
            foreach (Excerpt excerpt in group)
            {
                int matches = fences.Count(fence => ContainsMarker(fence, excerpt.Marker));
                if (matches != 1) failures.Add($"{group.Key} {excerpt.Key}: marker '{excerpt.Marker}' matched {matches} fences (expected 1).");
            }
        }

        // Assert
        failures.ShouldReportNothing("Registered excerpt markers no longer pick out exactly one fence each");
    }

    [Fact]
    public void SyncedExcerpts_AppearVerbatimInTheirSource()
    {
        // Arrange
        Func<string, IReadOnlyList<string>?> readSource = SourceAnchors.DiskReader(RepoRoot.Directory);
        List<string> drift = new();

        // Act: every non-blank line of a synced excerpt's fence must appear, in order, as a verbatim
        // substring of its source's lines; a miss means the quote has drifted from the committed file.
        foreach (IGrouping<string, Excerpt> group in Excerpts.GroupBy(static excerpt => excerpt.Doc))
        {
            IReadOnlyList<IReadOnlyList<string>> fences = Gate.Scan(group.Key);
            foreach (Excerpt excerpt in group)
            {
                if (excerpt.Source is null) continue;

                IReadOnlyList<string>? source = readSource(excerpt.Source);
                if (source is null)
                {
                    drift.Add($"{group.Key} {excerpt.Key}: source {excerpt.Source} does not exist.");
                    continue;
                }

                IReadOnlyList<string> fence = fences.Single(f => ContainsMarker(f, excerpt.Marker));
                string? unmatched = FirstLineNotInSource(fence, source);
                if (unmatched is not null)
                    drift.Add($"{group.Key} {excerpt.Key}: fence line '{unmatched}' is not a verbatim substring, in order, of {excerpt.Source}.");
            }
        }

        // Assert
        drift.ShouldReportNothing("Quoted excerpts no longer match their committed sources");
    }

    [Fact]
    public void EveryRegisteredDoc_YieldsAtLeastOneFence()
    {
        // Act & Assert: guard against the fence scanner silently matching nothing if a doc's quoting style
        // changes out from under this gate. Every registered doc is reported, not just the first to come
        // up empty.
        Gate.ShouldYieldFromEveryRegisteredDoc();
    }

    private static bool ContainsMarker(IReadOnlyList<string> fence, string marker)
    {
        return fence.Any(line => line.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Returns the first non-blank line of <paramref name="fence" /> that is not a verbatim ordinal
    ///     substring of some <paramref name="source" /> line at a strictly greater index than the previous
    ///     match (greedy first match after the previous), or <see langword="null" /> when every non-blank
    ///     fence line is found in order. Blank fence lines are skipped, so a quoted blank separator needs no
    ///     counterpart in the source. Nothing is trimmed: the docs quote with original indentation, so
    ///     containment must hold on the raw line.
    /// </summary>
    private static string? FirstLineNotInSource(IReadOnlyList<string> fence, IReadOnlyList<string> source)
    {
        var searchFrom = 0;
        foreach (string fenceLine in fence)
        {
            if (string.IsNullOrWhiteSpace(fenceLine)) continue;

            int found = -1;
            for (int index = searchFrom; index < source.Count; index++)
                if (source[index]
                    .Contains(fenceLine, StringComparison.Ordinal))
                {
                    found = index;
                    break;
                }

            if (found < 0) return fenceLine;
            searchFrom = found + 1;
        }

        return null;
    }

    /// <summary>
    ///     One registered excerpt: the <paramref name="Doc" /> it is quoted in, its <paramref name="Key" />,
    ///     the <paramref name="Marker" /> substring that picks out its fence within that doc, and the
    ///     committed <paramref name="Source" /> it is synced against, or <see langword="null" /> when the
    ///     excerpt is demonstration-exempt.
    /// </summary>
    private sealed record Excerpt(string Doc, string Key, string Marker, string? Source);
}
