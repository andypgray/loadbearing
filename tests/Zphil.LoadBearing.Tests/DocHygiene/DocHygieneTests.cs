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
///     because a fenced block quotes tool output whose idiom belongs to the tool. Two inventory checks
///     keep every reader doc a directory can carry inside the budgeted set.
/// </summary>
public sealed class DocHygieneTests
{
    private static readonly string[] InternalReferenceFreeDocs =
    [
        "GRAMMAR.md",
        "README.md",
        "ARCHITECTURE.md",
        "CHANGELOG.md",
        "AGENTS.md"
    ];

    private static readonly string[] BudgetDocs =
    [
        "README.md",
        "ARCHITECTURE.md",
        "CONTRIBUTING.md",
        "SECURITY.md",
        "src/Zphil.LoadBearing/README.md",
        "src/Zphil.LoadBearing.Packs.DotNet/README.md",
        "src/Zphil.LoadBearing.Roslyn/README.md",
        "src/Zphil.LoadBearing.Xunit/README.md",
        "examples/README.md",
        "examples/Meridian/README.md",
        "examples/Meridian/ADOPTING.md",
        "examples/Meridian/ARCHITECTURE.md",
        "examples/Meridian/hooks/README.md",
        "examples/Meridian.Quoting/README.md",
        "examples/Meridian.Quoting/ARCHITECTURE.md",
        "examples/Meridian.Operations/README.md",
        "examples/Meridian.Operations/ARCHITECTURE.md",
        "examples/Meridian.Interchange/README.md",
        "examples/Meridian.Interchange/ARCHITECTURE.md",
        "hooks/README.md"
    ];

    /// <summary>
    ///     The reader-facing filenames a directory can carry. Both inventory checks below sweep for
    ///     these rather than for <c>README.md</c> alone, which is what keeps a per-directory doc of any
    ///     of these names from shipping outside the budget gate.
    /// </summary>
    private static readonly string[] BudgetedFileNames = ["README.md", "ARCHITECTURE.md", "ADOPTING.md"];

    private static readonly string[] VoiceDocs =
    [
        .. BudgetDocs,
        "GRAMMAR.md",
        "CHANGELOG.md",
        "AGENTS.md"
    ];

    /// <summary>
    ///     References the published tree must never contain: the project's internal working-document
    ///     filenames, its working-notes directory, the labels its build was coordinated by, and the
    ///     digit-free vocabulary that coordination is narrated in.
    /// </summary>
    /// <remarks>
    ///     The digit-bearing and filename forms stay case-sensitive, which is what lets ordinary prose
    ///     share a word with a label. The narration and vocabulary forms are case-insensitive, because
    ///     narration is written in sentence case as often as not — so they are written to be precise
    ///     instead. The article in the narration form is the whole of its precision: a bare
    ///     <c>\bphase\b</c> also reds a concurrency term and this very summary, while requiring
    ///     <em>this/the/that</em> in front of it reds only prose about a stage of the build.
    /// </remarks>
    internal static readonly Regex[] InternalReferencePatterns =
    [
        new(@"\bPhase\s*-?\s*[0-9]"),
        new(@"\bWP-?\s*[0-9]"),
        new(@"\bWP\b"),
        new(@"\b(this|the|that)\s+(current|next|last|previous|final|first)?\s*phase\b", RegexOptions.IgnoreCase),
        new(@"\btask-[0-9]+\b", RegexOptions.IgnoreCase),
        new(@"\bwork package\b", RegexOptions.IgnoreCase),
        new(@"\btracker\b", RegexOptions.IgnoreCase),
        new(@"\bDESIGN\.md\b"),
        new(@"\bPLAN\.md\b"),
        new(@"\bPLAN-ARCHIVE\.md\b"),
        new(@"\bEXAMPLES\.md\b"),
        new(@"\bGUIDANCE-PACK\.md\b"),
        new(@"\bEVALUATION\.md\b"),
        new(@"\boutside-review\.md\b"),
        new(@"\bdocs/")
    ];

    /// <summary>
    ///     The development environment a published tree never names: a machine path, a home directory,
    ///     or a personal identity.
    /// </summary>
    /// <remarks>
    ///     These are shapes rather than names on purpose. A private sibling repository can only be
    ///     matched by name, and a denylist that spells such a name out publishes it permanently — so
    ///     those patterns are read from an untracked local file instead, and this array holds only
    ///     what can be described without disclosing anything. See <see cref="LocalPrivatePatterns" />.
    ///     Known-benign forms these are drawn tightly enough to leave alone: an environment-variable
    ///     root with no drive letter, and a drive-rooted path to a machine-independent location such
    ///     as an installed toolchain or a build output directory.
    /// </remarks>
    internal static readonly Regex[] PrivateEnvironmentPatterns =
    [
        new(@"[A-Za-z]:[\\/](Users|source|repos)\b", RegexOptions.IgnoreCase),
        new(@"/home/[a-z]"),
        new(@"/Users/[a-z]"),
        new(@"%USERPROFILE%", RegexOptions.IgnoreCase),
        new(@"@gmail\.com", RegexOptions.IgnoreCase)
    ];

    /// <summary>
    ///     Prose about running the development tooling: the local workflow one maintainer follows,
    ///     which documents a machine's habits to strangers and dates the day the tool changes.
    /// </summary>
    /// <remarks>
    ///     One pattern rather than a list, spelled the way the publication review spells it, so the gate
    ///     and the review can never disagree about what counts. Case-insensitive because every token is
    ///     a product or command name that prose capitalises however it likes. What it does not cover is
    ///     the ordinary .NET suppression a source file carries above a deliberate site: that is source
    ///     rather than narration, and the source arm blanks those before it reads.
    /// </remarks>
    internal static readonly Regex[] DevToolingPatterns =
    [
        new(@"resharper|jetbrains|inspectcode|cleanupcode|\bjb\b|cleanup profile|dotsettings",
            RegexOptions.IgnoreCase)
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
        string content = RepoRoot.ReadText(relativePath);

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(content, InternalReferencePatterns);

        // Assert
        hits.ShouldReportNothing($"{relativePath} names internal working references");
    }

    [Theory]
    [MemberData(nameof(VoiceDocsCases))]
    public void ReaderDoc_HoldsHouseVoice(string relativePath)
    {
        // Arrange
        string content = RepoRoot.ReadText(relativePath);

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(content, HouseVoicePatterns);

        // Assert
        hits.ShouldReportNothing($"{relativePath} uses off-voice wording");
    }

    [Theory]
    [MemberData(nameof(BudgetDocsCases))]
    public void ReaderDoc_StaysWithinProseBudgets(string relativePath)
    {
        // Arrange
        string prose = DocProse.StripFences(RepoRoot.ReadText(relativePath));

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
    public void ExampleAndPackageReaderDocs_AreAllInsideTheBudgetGate()
    {
        // Arrange
        List<string> uncovered = new();

        // Act: every immediate child of examples/ and src/ that carries a top-level reader doc must be
        // budgeted, so a new example or package doc cannot slip past the gate unnoticed.
        foreach (string parent in new[] { "examples", "src" })
        {
            string parentDirectory = RepoRoot.Absolute(parent);
            if (!Directory.Exists(parentDirectory)) continue;

            foreach (string childDirectory in Directory.GetDirectories(parentDirectory))
            foreach (string fileName in BudgetedFileNames)
            {
                string readerDoc = Path.Combine(childDirectory, fileName);
                if (!File.Exists(readerDoc)) continue;

                string relativePath = RepoRoot.Relative(readerDoc);
                if (!BudgetDocs.Contains(relativePath)) uncovered.Add(relativePath);
            }
        }

        // Assert
        uncovered.ShouldReportNothing("Reader doc(s) under examples/ or src/ are outside the budgeted set");
    }

    [Fact]
    public void EveryTrackedReaderDoc_IsInsideTheBudgetGate()
    {
        // Act: the walk above descends one level under examples/ and src/, which is how a reader doc at
        // a new top-level directory stayed invisible to it. Asking git for the set closes that blind
        // spot without another directory name to keep up to date.
        List<string> uncovered = TrackedFiles.All
            .Where(static path => BudgetedFileNames.Contains(Path.GetFileName(path)))
            .Where(static path => !BudgetDocs.Contains(path))
            .ToList();

        // Assert
        uncovered.ShouldReportNothing("Tracked reader doc(s) are outside the budgeted set");
    }

    private static TheoryData<string> ToTheoryData(string[] docs)
    {
        TheoryData<string> data = new();
        foreach (string doc in docs) data.Add(doc);

        return data;
    }
}
