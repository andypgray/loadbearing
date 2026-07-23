using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The documentation gates over this repository's own published docs. The published language
///     spec stays free of the project's internal working references; the reader-facing docs keep the
///     house voice rather than describing a codebase in off-voice terms; and the reader-facing docs
///     hold the house prose budgets — a bounded em-dash count and a bounded count of the
///     "deliberately" and "intentionally" tics, both measured over prose outside fenced code blocks,
///     because a fenced block quotes tool output whose idiom belongs to the tool. An inventory check
///     keeps every example and package README inside the budgeted set.
/// </summary>
public sealed class DocHygieneTests
{
    private static readonly string[] InternalReferenceFreeDocs =
    [
        "GRAMMAR.md",
        "README.md",
        "CHANGELOG.md"
    ];

    private static readonly string[] BudgetDocs =
    [
        "README.md",
        "CONTRIBUTING.md",
        "SECURITY.md",
        "src/Zphil.LoadBearing/README.md",
        "src/Zphil.LoadBearing.Roslyn/README.md",
        "src/Zphil.LoadBearing.Xunit/README.md",
        "examples/README.md",
        "examples/Meridian/README.md",
        "examples/Meridian/ADOPTING.md",
        "examples/Meridian.Quoting/README.md",
        "examples/Meridian.Operations/README.md",
        "examples/Meridian.Interchange/README.md"
    ];

    private static readonly string[] VoiceDocs =
    [
        ..BudgetDocs,
        "GRAMMAR.md",
        "CHANGELOG.md",
        "AGENTS.md"
    ];

    /// <summary>
    ///     References the published documentation must never contain: the project's internal
    ///     working-document filenames, its working-notes directory, and internal phase and work-item
    ///     labels. The patterns are case-sensitive so ordinary prose that merely shares a word is left
    ///     alone.
    /// </summary>
    internal static readonly Regex[] InternalReferencePatterns =
    [
        new(@"\bPhase\s+[0-9]"),
        new(@"\bWP\s*[0-9]"),
        new(@"\bDESIGN\.md\b"),
        new(@"\bPLAN\.md\b"),
        new(@"\bPLAN-ARCHIVE\.md\b"),
        new(@"\bEXAMPLES\.md\b"),
        new(@"\bGUIDANCE-PACK\.md\b"),
        new(@"\bdocs/")
    ];

    /// <summary>Off-voice descriptions of a codebase that the reader-facing docs never use.</summary>
    internal static readonly Regex[] HouseVoicePatterns =
    [
        new(@"\bmessy\b", RegexOptions.IgnoreCase),
        new(@"\bspaghetti\b", RegexOptions.IgnoreCase),
        new(@"\bball of mud\b", RegexOptions.IgnoreCase)
    ];

    public static TheoryData<string> InternalReferenceFreeDocsCases => ToTheoryData(InternalReferenceFreeDocs);

    public static TheoryData<string> VoiceDocsCases => ToTheoryData(VoiceDocs);

    public static TheoryData<string> BudgetDocsCases => ToTheoryData(BudgetDocs);

    [Theory]
    [MemberData(nameof(InternalReferenceFreeDocsCases))]
    public void PublishedDoc_HasNoInternalWorkingReferences(string relativePath)
    {
        // Arrange
        string content = ReadDoc(relativePath);

        // Act
        var hits = DocProse.FindForbidden(content, InternalReferencePatterns);

        // Assert
        hits.ShouldBeEmpty($"{relativePath} names internal working references:\n{string.Join("\n", hits)}");
    }

    [Theory]
    [MemberData(nameof(VoiceDocsCases))]
    public void ReaderDoc_HoldsHouseVoice(string relativePath)
    {
        // Arrange
        string content = ReadDoc(relativePath);

        // Act
        var hits = DocProse.FindForbidden(content, HouseVoicePatterns);

        // Assert
        hits.ShouldBeEmpty($"{relativePath} uses off-voice wording:\n{string.Join("\n", hits)}");
    }

    [Theory]
    [MemberData(nameof(BudgetDocsCases))]
    public void ReaderDoc_StaysWithinProseBudgets(string relativePath)
    {
        // Arrange
        string prose = DocProse.StripFences(ReadDoc(relativePath));

        // Act
        int words = DocProse.CountWords(prose);
        int emDashes = DocProse.CountEmDashes(prose);
        int tics = DocProse.CountTics(prose);
        var emDashBudget = (int)Math.Ceiling(words / 1000.0);

        // Assert
        emDashes.ShouldBeLessThanOrEqualTo(
            emDashBudget,
            $"{relativePath}: {emDashes} em-dashes across {words} words of prose (budget {emDashBudget}).");
        tics.ShouldBeLessThanOrEqualTo(
            2,
            $"{relativePath}: {tics} deliberately/intentionally occurrences in prose (budget 2).");
    }

    [Fact]
    public void ExampleAndPackageReadmes_AreAllInsideTheBudgetGate()
    {
        // Arrange
        List<string> uncovered = new();

        // Act: every immediate child of examples/ and src/ that carries a top-level README must be
        // budgeted, so a new example or package README cannot slip past the gate unnoticed.
        foreach (string parent in new[] { "examples", "src" })
        {
            string parentDirectory = Absolute(parent);
            if (!Directory.Exists(parentDirectory)) continue;

            foreach (string childDirectory in Directory.GetDirectories(parentDirectory))
            {
                string readme = Path.Combine(childDirectory, "README.md");
                if (!File.Exists(readme)) continue;

                string relativePath = ToRepoRelative(readme);
                if (!BudgetDocs.Contains(relativePath)) uncovered.Add(relativePath);
            }
        }

        // Assert
        uncovered.ShouldBeEmpty(
            $"README(s) under examples/ or src/ are outside the budgeted set:\n{string.Join("\n", uncovered)}");

        const string adopting = "examples/Meridian/ADOPTING.md";
        if (File.Exists(Absolute(adopting))) BudgetDocs.ShouldContain(adopting, $"{adopting} exists but is outside the budgeted set.");
    }

    private static string ReadDoc(string relativePath)
    {
        return File.ReadAllText(Absolute(relativePath));
    }

    private static string Absolute(string relativePath)
    {
        return Path.Combine(RepoRoot.Directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ToRepoRelative(string absolutePath)
    {
        return Path.GetRelativePath(RepoRoot.Directory, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static TheoryData<string> ToTheoryData(string[] docs)
    {
        TheoryData<string> data = new();
        foreach (string doc in docs) data.Add(doc);

        return data;
    }
}