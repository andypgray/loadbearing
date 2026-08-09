using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The quote-sync gate over the root README's fenced excerpts. The landing page walks this
///     repository's own spec across surface after surface, quoting committed sources inside fenced code
///     blocks — a starter rule from the quoting example's spec, two rules of the self-spec, the rendered
///     <c>AGENTS.md</c> bullet one of them generates, the adapter class that runs them all as tests, the
///     rendered Mermaid diagram of this solution — and
///     this gate holds each quote to the file it was cut from: every non-blank line of the excerpt's fence
///     must appear, in order, as a verbatim substring of its source, so an edit to the source that the
///     README does not follow fails the suite instead of publishing a stale quote. Five excerpts are
///     captured tool output with no committed source; they are demonstration-exempt, held only to the
///     requirement that their fences still exist, so an exemption cannot silently go dead.
/// </summary>
public sealed class RootReadmeQuoteSyncTests
{
    private const string RootReadme = "README.md";
    private const string SelfSpec = "arch/Zphil.LoadBearing.ArchSpec/LoadBearingArchSpec.cs";

    /// <summary>
    ///     The root README's quoted excerpts, each keyed by a distinctive marker substring that must pick
    ///     out exactly one of the README's fences, and paired with the committed source the fence is synced
    ///     against. An excerpt with a null source is demonstration-exempt: nothing is synced against it,
    ///     but its fence must still exist.
    /// </summary>
    private static readonly Excerpt[] Excerpts =
    [
        new(
            // The landing example: quoted dedented from the example spec, which the substring check
            // permits because a dedented line is still contained in its indented source line.
            "starter-rule",
            "arch.Rule(\"layering/domain-independent\")",
            "examples/Meridian.Quoting/arch/Meridian.Quoting.ArchSpec/QuotingArchSpec.cs"),
        new(
            "self-spec-rule",
            "arch.Rule(\"cli/no-stdout\")",
            SelfSpec),
        new(
            // The rendered bullet carries the derived rule sentence and the rule's Because on one line,
            // which the FAIL stanza below splits across two; that join is what makes this marker pick out
            // the AGENTS.md fence rather than the hook report.
            "agents-block",
            "`Console.WriteLine()`. Stdout is a protocol channel",
            "AGENTS.md"),
        new(
            "adapter-test",
            "class AdapterSelfSpecTests : ArchRuleTests<LoadBearingArchSpec>",
            "tests/Zphil.LoadBearing.Tests/Dogfood/AdapterSelfSpecTests.cs"),
        new(
            "migrate-rule",
            "arch.Rule(\"mcp/env-through-seam\")",
            SelfSpec),
        new(
            // The one tool-output fence with a committed counterpart: `render --diagram` writes the block
            // into ARCHITECTURE.md, so the README quote is synced rather than demonstration-exempt. The
            // marker is the accessible title rather than `flowchart LR`, which stopped being distinctive
            // the moment that one block grew a second fence.
            "graph-diagram",
            "accTitle: Codebase survey:",
            "ARCHITECTURE.md"),
        new(
            // The law fence out of the same block, drawn from the spec rather than from the codebase.
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
            "hook-report",
            "FAIL cli/no-stdout",
            null),
        new(
            "sarif-result",
            "\"ruleId\": \"mcp/env-through-seam\"",
            null),
        new(
            "check-json",
            "\"posture\": \"migrate\"",
            null),
        new(
            "graph-survey",
            "192 types; references: (none)",
            null),
        new(
            "legacy-check",
            "Classic.Billing/BillingCalculator.cs:10",
            null)
    ];

    [Fact]
    public void EachExcerpt_MatchesExactlyOneFence()
    {
        // Arrange
        var fences = SourceAnchors.Fences(ReadRootReadme());
        List<string> failures = new();

        // Act: every excerpt's marker — including the demonstration-exempt one — must pick out exactly one
        // fence; zero means the excerpt was dropped or reworded, more than one means it is no longer distinctive.
        foreach (Excerpt excerpt in Excerpts)
        {
            int matches = fences.Count(fence => ContainsMarker(fence, excerpt.Marker));
            if (matches != 1) failures.Add($"{excerpt.Key}: marker '{excerpt.Marker}' matched {matches} fences (expected 1).");
        }

        // Assert
        failures.ShouldBeEmpty(
            $"Root README excerpt markers no longer pick out exactly one fence each:\n{string.Join("\n", failures)}");
    }

    [Fact]
    public void SyncedExcerpts_AppearVerbatimInTheirSource()
    {
        // Arrange
        var fences = SourceAnchors.Fences(ReadRootReadme());
        List<string> drift = new();

        // Act: every non-blank line of a synced excerpt's fence must appear, in order, as a verbatim
        // substring of its source's lines; a miss means the README quote has drifted from the committed file.
        foreach (Excerpt excerpt in Excerpts)
        {
            if (excerpt.Source is null) continue;

            var fence = fences.Single(f => ContainsMarker(f, excerpt.Marker));
            string? unmatched = FirstLineNotInSource(fence, ReadSource(excerpt.Source));
            if (unmatched is not null)
                drift.Add($"{excerpt.Key}: fence line '{unmatched}' is not a verbatim substring, in order, of {excerpt.Source}.");
        }

        // Assert
        drift.ShouldBeEmpty(
            $"Root README excerpts no longer match their committed sources:\n{string.Join("\n", drift)}");
    }

    [Fact]
    public void RootReadme_YieldsAtLeastOneFence()
    {
        // Arrange
        var fences = SourceAnchors.Fences(ReadRootReadme());

        // Act & Assert: guard against the fence scanner silently matching nothing if the README's quoting
        // style changes out from under this gate.
        fences.ShouldNotBeEmpty(
            "The root README yielded no fenced code blocks; the fence scanner may be silently matching nothing.");
    }

    private static string ReadRootReadme()
    {
        return File.ReadAllText(RepoRoot.Absolute(RootReadme));
    }

    private static IReadOnlyList<string> ReadSource(string repoRelative)
    {
        return File.ReadAllLines(RepoRoot.Absolute(repoRelative));
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
    ///     counterpart in the source. Nothing is trimmed: the README quotes with original indentation, so
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
                if (source[index].Contains(fenceLine, StringComparison.Ordinal))
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
    ///     One registered README excerpt: its <paramref name="Key" />, the <paramref name="Marker" />
    ///     substring that picks out its fence, and the committed <paramref name="Source" /> it is synced
    ///     against, or <see langword="null" /> when the excerpt is demonstration-exempt.
    /// </summary>
    private sealed record Excerpt(string Key, string Marker, string? Source);
}
