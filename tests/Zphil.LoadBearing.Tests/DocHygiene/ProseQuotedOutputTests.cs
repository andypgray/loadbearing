using System.Text.RegularExpressions;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The gate over the two quoted shapes a fence scanner cannot reach. A walkthrough sometimes quotes
///     one line of captured output inline in a sentence rather than in a block, which puts it outside
///     every gate here by construction; and one capture bakes wall-clock timings, which nothing can
///     reproduce. Both are registered with the reason they are out, and both carry a guard that fails the
///     day the reason stops being true — a fenced summary should be held by the fenced gates, and a
///     timing capture that has lost its timings is no longer the thing the exemption describes. A free
///     arithmetic sweep runs over every check summary in every tracked doc, fenced or not, because
///     <c>passed + failed + skipped</c> must equal <c>checked</c> wherever the line appears.
/// </summary>
/// <remarks>
///     Deliberately not a count gate: <see cref="GrandfatheredCountSyncTests" /> holds numbers to the
///     baselines they came from, and the rule totals in a summary line have no such source. Checking
///     <c>Checked N rules</c> against the rendered bullets would hold six of the eight summary sites and
///     need a hand-pinned constant for the other two, which is the hand-copied number these gates exist
///     to remove.
/// </remarks>
public sealed class ProseQuotedOutputTests
{
    private const string QuotingReadme = "examples/Meridian.Quoting/README.md";
    private const string InterchangeReadme = "examples/Meridian.Interchange/README.md";
    private const string OperationsReadme = "examples/Meridian.Operations/README.md";

    /// <summary>A check or status summary line, wherever it appears.</summary>
    private static readonly Regex SummaryLine =
        new(@"Checked (?<checked>\d+) rules: (?<passed>\d+) passed, (?<failed>\d+) failed, (?<skipped>\d+) skipped",
            RegexOptions.CultureInvariant);

    /// <summary>
    ///     A wall-clock timing as the test runner prints one — a per-case duration or a run total. The
    ///     shape is what is pinned, never the value: a capture whose numbers move is still a timing
    ///     capture, and a capture that has lost them is not.
    /// </summary>
    private static readonly Regex TimingToken =
        new(@"\[(?:< )?\d+(?:\.\d+)? ?(?:ms|s)\]|Total time: \d+(?:\.\d+)? Seconds", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Summaries quoted inline in a sentence. Each is exempt from the fenced gates because no fence
    ///     holds it, and each is held here instead: the sentence must still read this way, and the doc
    ///     must still not carry the same text in a fence — the day it does, the exemption is dead and the
    ///     quote should be promoted into the fenced gates rather than counted as covered twice.
    /// </summary>
    private static readonly (string Doc, string Summary)[] ProseSummaries =
    [
        (QuotingReadme, "Checked 9 rules: 8 passed, 1 failed, 0 skipped (2 violations, 0 warnings)"),
        (InterchangeReadme, "Checked 12 rules: 12 passed, 0 failed, 0 skipped (0 violations, 0 warnings)"),
        (OperationsReadme, "Checked 10 rules: 9 passed, 0 failed, 1 skipped (0 violations, 0 warnings)")
    ];

    /// <summary>
    ///     Captures that are permanently unreproducible, with the reason. Each is held to its fence still
    ///     existing and still carrying what makes it unreproducible, so the entry dies loudly rather than
    ///     outliving the block it excuses.
    /// </summary>
    private static readonly (string Doc, string Marker, string Reason)[] TimedCaptures =
    [
        (QuotingReadme,
            "Test Run Successful.",
            "The adapter's `dotnet test` capture bakes wall-clock timings — the workspace load billed to "
            + "whichever case runs first, and the run total — which no two runs reproduce and which are the "
            + "point of the paragraph beneath it.")
    ];

    [Fact]
    public void ProseQuotedSummaries_StillReadAsRegistered()
    {
        // Arrange
        List<string> drift = new();

        // Act: a rewrite of the sentence must fail here, because nothing else is watching it.
        foreach ((string doc, string summary) in ProseSummaries)
            if (!RepoRoot.ReadText(doc)
                    .Contains(summary, StringComparison.Ordinal))
                drift.Add($"{doc} no longer quotes '{summary}'.");

        // Assert
        drift.ShouldReportNothing("These registered inline summaries have been reworded");
    }

    [Fact]
    public void ProseQuotedSummaries_AreStillOutsideEveryFence()
    {
        // Arrange
        List<string> fenced = new();

        // Act: the inverted guard. These are registered because a fence scanner cannot see them; the day
        // one is fenced, the fenced gates cover it and this entry should go.
        foreach ((string doc, string summary) in ProseSummaries)
            if (SourceAnchors.Fences(RepoRoot.ReadText(doc))
                .Any(fence => fence.Any(line => line.Contains(summary, StringComparison.Ordinal))))
                fenced.Add($"{doc} now quotes '{summary}' inside a fence; remove the exemption and let the fenced gates hold it.");

        // Assert
        fenced.ShouldReportNothing("These inline summaries are no longer inline");
    }

    [Fact]
    public void EveryTrackedDocQuotingAnInlineSummary_IsRegistered()
    {
        // Arrange: the registry above is hand-written, so the failure it cannot see is a new doc quoting a
        // summary in prose — invisible to every other gate here by construction.
        List<string> unregistered = new();

        // Act
        foreach (string doc in TrackedFiles.Markdown)
        foreach ((string line, int number) in SourceAnchors.UnfencedLines(RepoRoot.ReadText(doc)))
        {
            if (!SummaryLine.IsMatch(line)) continue;

            if (!ProseSummaries.Any(entry => entry.Doc == doc && line.Contains(entry.Summary, StringComparison.Ordinal)))
                unregistered.Add($"{doc}:{number} quotes a check summary in prose but is not registered: {line.Trim()}");
        }

        // Assert
        unregistered.ShouldReportNothing("These tracked docs quote a check summary where no fence scanner can see it");
    }

    [Fact]
    public void EveryQuotedCheckSummary_AddsUp()
    {
        // Arrange: fenced or not, registered or not — the rule counts of a summary line are internally
        // checkable, and a capture that was edited by hand rather than re-run usually stops adding up.
        List<string> wrong = new();

        // Act
        foreach (string doc in TrackedFiles.Markdown)
        foreach ((string line, int number) in Lines(doc))
        {
            Match match = SummaryLine.Match(line);
            if (!match.Success) continue;

            int checkedRules = Group(match, "checked");
            int total = Group(match, "passed") + Group(match, "failed") + Group(match, "skipped");
            if (total != checkedRules) wrong.Add($"{doc}:{number} reports {checkedRules} rules checked but {total} accounted for: {line.Trim()}");
        }

        // Assert
        wrong.ShouldReportNothing("These quoted check summaries do not add up");
    }

    [Fact]
    public void TimingBakedCaptures_StillExistAndStillCarryTheirTimings()
    {
        // Arrange
        List<string> dead = new();

        // Act: an exemption is only honest while the thing it excuses is still there. A marker matching
        // no fence means the block went away; a fence with no timing token means it is reproducible now
        // and should be held like every other capture.
        foreach ((string doc, string marker, string _) in TimedCaptures)
        {
            IReadOnlyList<string>[] fences = SourceAnchors.Fences(RepoRoot.ReadText(doc))
                .Where(fence => fence.Any(line => line.Contains(marker, StringComparison.Ordinal)))
                .ToArray();
            if (fences.Length != 1)
            {
                dead.Add($"{doc}: marker '{marker}' matched {fences.Length} fences (expected 1).");
                continue;
            }

            if (!fences[0]
                    .Any(line => TimingToken.IsMatch(line)))
                dead.Add($"{doc}: the fence at '{marker}' no longer carries a wall-clock timing; it is reproducible now, so the exemption should go.");
        }

        // Assert
        dead.ShouldReportNothing("These permanently-exempt captures no longer match what the exemption describes");
    }

    private static IEnumerable<(string Text, int Number)> Lines(string doc)
    {
        return RepoRoot.ReadText(doc)
            .NormalizedLines()
            .Split('\n')
            .Select(static (text, index) => (text, index + 1));
    }

    private static int Group(Match match, string name)
    {
        return int.Parse(match.Groups[name]
            .Value);
    }
}
