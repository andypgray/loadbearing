using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Unit and negative tests for <see cref="WrittenPaths" />. They pin the fenced write-report scanner —
///     including the prose line it must leave alone — the solution-relative path composition, and the
///     classifier that decides whether a quoted path still names a published file, so the gate and these
///     tests exercise the same code path.
/// </summary>
public sealed class WrittenPathsTests
{
    [Fact]
    public void Extract_FencedWriteReport_CapturedWithLocationAndParts()
    {
        // Arrange
        string[] lines =
        [
            "intro prose",
            "```text",
            "$ loadbearing render examples/Meridian/Meridian.slnx",
            "unchanged src/Meridian.Web/AGENTS.md",
            "```",
            "wrote arch/baselines/outside.json"
        ];
        string doc = string.Join("\n", lines);

        // Act
        IReadOnlyList<WrittenPath> written = WrittenPaths.Extract("d.md", doc);

        // Assert: the line outside the fence is prose restating the capture, not the capture itself.
        WrittenPath path = written.ShouldHaveSingleItem();
        path.Doc.ShouldBe("d.md");
        path.DocLine.ShouldBe(4);
        path.Label.ShouldBe("unchanged");
        path.Path.ShouldBe("src/Meridian.Web/AGENTS.md");
    }

    [Theory]
    [InlineData("wrote")]
    [InlineData("unchanged")]
    public void Extract_EitherReportVerb_Captured(string label)
    {
        // Arrange
        string doc = string.Join(
            "\n",
            "```text",
            $"{label} arch/baselines/time/inject-clock.json",
            "```");

        // Act
        IReadOnlyList<WrittenPath> written = WrittenPaths.Extract("d.md", doc);

        // Assert
        written.ShouldHaveSingleItem()
            .Label.ShouldBe(label);
    }

    [Fact]
    public void Extract_FencedProseWithNoPathToken_Ignored()
    {
        // Arrange: a fenced line can open with the verb and still name no file. Without the narrowing
        // guard this would be reported as a stranded path forever, because no file can ever match it.
        string doc = string.Join(
            "\n",
            "```text",
            "unchanged behaviour",
            "wrote nothing",
            "```");

        // Act
        IReadOnlyList<WrittenPath> written = WrittenPaths.Extract("d.md", doc);

        // Assert
        written.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_BareFileNameAtSolutionRoot_Captured()
    {
        // Arrange: the root card is reported with no directory at all, and the extension is what makes it
        // a path rather than prose.
        string doc = string.Join("\n", "```text", "unchanged AGENTS.md", "```");

        // Act
        IReadOnlyList<WrittenPath> written = WrittenPaths.Extract("d.md", doc);

        // Assert
        written.ShouldHaveSingleItem()
            .Path.ShouldBe("AGENTS.md");
    }

    [Fact]
    public void Extract_MultipleFencedBlocks_AllPathsCaptured()
    {
        // Arrange
        string[] lines =
        [
            "```text",
            "wrote arch/baselines/data-access/no-inline-sql.json",
            "```",
            "prose between blocks",
            "~~~",
            "unchanged AGENTS.md",
            "~~~"
        ];
        string doc = string.Join("\n", lines);

        // Act
        IReadOnlyList<WrittenPath> written = WrittenPaths.Extract("d.md", doc);

        // Assert
        written.Count.ShouldBe(2);
        written[0]
            .DocLine.ShouldBe(2);
        written[0]
            .Path.ShouldBe("arch/baselines/data-access/no-inline-sql.json");
        written[1]
            .DocLine.ShouldBe(6);
        written[1]
            .Path.ShouldBe("AGENTS.md");
    }

    [Fact]
    public void Classify_TrackedUnderTheExampleRoot_Matched()
    {
        // Arrange
        WrittenPath written = new("d.md", 12, "unchanged", "src/Meridian.Web/AGENTS.md");
        IReadOnlySet<string> tracked = WrittenPaths.TrackedSet(["examples/Meridian/src/Meridian.Web/AGENTS.md"]);

        // Act
        WrittenPaths.PathResult result = WrittenPaths.Classify(written, "examples/Meridian", tracked);

        // Assert
        result.Bucket.ShouldBe(WrittenPaths.PathBucket.Matched);
        result.Failure.ShouldBeNull();
    }

    [Fact]
    public void Classify_EmptyExampleRoot_ResolvesAgainstTheRepositoryRoot()
    {
        // Arrange: a doc quoting a run over this repository's own solution reports paths that are already
        // repository-relative.
        WrittenPath written = new("README.md", 12, "wrote", "arch/baselines/mcp/env-through-seam.json");
        IReadOnlySet<string> tracked = WrittenPaths.TrackedSet(["arch/baselines/mcp/env-through-seam.json"]);

        // Act
        WrittenPaths.PathResult result = WrittenPaths.Classify(written, string.Empty, tracked);

        // Assert
        result.Bucket.ShouldBe(WrittenPaths.PathBucket.Matched);
    }

    [Fact]
    public void Classify_RenamedFile_Untracked()
    {
        // Arrange: the rename this gate exists to catch — the walkthrough still quotes the old name.
        WrittenPath written = new("d.md", 12, "wrote", "arch/baselines/time/inject-clock.json");
        IReadOnlySet<string> tracked = WrittenPaths.TrackedSet(["examples/Meridian/arch/baselines/time/injected-clock.json"]);

        // Act
        WrittenPaths.PathResult result = WrittenPaths.Classify(written, "examples/Meridian", tracked);

        // Assert: the failure names the doc, the line, the quoted line and the path that was looked for.
        result.Bucket.ShouldBe(WrittenPaths.PathBucket.Untracked);
        result.Failure!.ShouldContain("d.md:12");
        result.Failure!.ShouldContain("wrote arch/baselines/time/inject-clock.json");
        result.Failure!.ShouldContain("examples/Meridian/arch/baselines/time/inject-clock.json");
    }

    [Fact]
    public void Classify_PathTrackedUnderADifferentExample_Untracked()
    {
        // Arrange: the root is part of the identity, so a sibling example's file cannot answer for this
        // walkthrough's quoted path.
        WrittenPath written = new("d.md", 12, "unchanged", "AGENTS.md");
        IReadOnlySet<string> tracked = WrittenPaths.TrackedSet(["examples/Meridian.Quoting/AGENTS.md"]);

        // Act
        WrittenPaths.PathResult result = WrittenPaths.Classify(written, "examples/Meridian", tracked);

        // Assert
        result.Bucket.ShouldBe(WrittenPaths.PathBucket.Untracked);
    }
}
