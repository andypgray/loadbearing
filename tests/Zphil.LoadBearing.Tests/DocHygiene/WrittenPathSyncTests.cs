using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The quote-sync gate over quoted write reports. The example walkthroughs quote captured
///     <c>render</c> and <c>baseline</c> output, whose every line names a file the verb touched:
///     <c>wrote arch/baselines/&lt;rule&gt;.json</c> when the bytes changed, <c>unchanged &lt;card&gt;</c>
///     when they did not. Nothing regenerates a hand-written doc, so this gate holds each quoted path to
///     the tracked file it names: a renamed baseline or a moved <c>AGENTS.md</c> fails the suite instead
///     of publishing a walkthrough that promises a file the repository no longer has.
/// </summary>
/// <remarks>
///     Sibling to <see cref="RuleQuoteSyncTests" /> (the quoted rule sentence) and
///     <see cref="QuoteSyncTests" /> (the whole quoted fence). This one holds the third shape in the same
///     captures — the file list — and like them it reads only committed bytes: no workspace load, no CLI
///     invocation, so it stays cheap and parallel-safe.
/// </remarks>
public sealed class WrittenPathSyncTests
{
    /// <summary>
    ///     The write-report-quoting docs, each paired with the example root its quoted paths hang off —
    ///     the verbs report relative to the solution directory, which for an example is that example's
    ///     root.
    /// </summary>
    private static readonly (string Doc, string ExampleRoot)[] WriteDocs =
    [
        // The adoption walkthrough captures both verbs: `baseline --init` writing four baselines, then
        // `render` reporting the four committed cards unchanged.
        ("examples/Meridian/ADOPTING.md", "examples/Meridian"),
        ("examples/Meridian.Operations/README.md", "examples/Meridian.Operations")
    ];

    private static readonly DocGate<WrittenPath> Gate = new(
        WriteDocs.Select(static entry => entry.Doc)
            .ToArray(),
        ExtractDoc,
        quotes: "write reports");

    [Fact]
    public void QuotedWritePaths_NameTrackedFiles()
    {
        // Arrange
        IReadOnlySet<string> tracked = WrittenPaths.TrackedSet(TrackedFiles.All);
        List<string> stranded = new();

        // Act: every quoted path, resolved against its own example root, must be a file git tracks.
        foreach ((string doc, string exampleRoot) in WriteDocs)
        foreach (WrittenPath written in Gate.Scan(doc))
        {
            WrittenPaths.PathResult result = WrittenPaths.Classify(written, exampleRoot, tracked);
            if (result.Bucket != WrittenPaths.PathBucket.Matched) stranded.Add(result.Failure!);
        }

        // Assert
        stranded.ShouldReportNothing("Quoted write reports name files this repository no longer publishes");
    }

    [Fact]
    public void EveryWritePathDoc_YieldsAtLeastOnePath()
    {
        // Act & Assert: guard against the scanner silently matching nothing if a doc's quoting style changes.
        Gate.ShouldYieldFromEveryRegisteredDoc();
    }

    [Fact]
    public void BothReportVerbs_AppearInTheQuotedCorpus()
    {
        // Arrange & Act: the corpus must keep exercising both alternatives, or half the pattern is dead
        // and nobody finds out until the day a doc quotes the other one.
        HashSet<string> labels = WriteDocs
            .SelectMany(entry => Gate.Scan(entry.Doc))
            .Select(static written => written.Label)
            .ToHashSet(StringComparer.Ordinal);

        // Assert
        labels.ShouldBe(["wrote", "unchanged"], ignoreOrder: true);
    }

    [Fact]
    public void EveryTrackedDocQuotingWritePaths_IsRegistered()
    {
        // Act & Assert: the registry above is hand-written, so the failure it cannot see is a doc that
        // quotes a write report and was never added to it — a whole walkthrough silently outside the gate.
        Gate.ShouldFindNothingOutsideTheRegistry(
            counted: "write report line(s)",
            swept: "write reports",
            authority: "the tracked file set");
    }

    private static IReadOnlyList<WrittenPath> ExtractDoc(string doc)
    {
        string text = RepoRoot.ReadText(doc);
        return WrittenPaths.Extract(doc, text);
    }
}
