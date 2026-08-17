using System.Xml.Linq;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The shape gate over the four example solutions' committed <c>ARCHITECTURE.md</c> drawings. The
///     split is worth stating: byte-currency belongs to CI's examples job, which builds each example,
///     re-runs <c>render --diagram</c> and asserts a zero diff across <c>examples/</c>; that job is
///     ubuntu-only, so a Windows checkout gets no signal from it at all. This gate reads the committed
///     files and nothing else, so it runs everywhere and names the failures a byte comparison would
///     only report after somebody re-rendered: a missing fence, a caption naming the wrong spec, or a
///     survey that quietly drew the spec project's own package references.
/// </summary>
/// <remarks>
///     DocHygiene rather than Dogfood, because this needs no workspace and must not join
///     <c>SelfSpecTests</c>' serial collection. That class excludes <c>examples/</c> from its render
///     and orphan gates on purpose, since those blocks belong to other specs; a file-shape assertion
///     over a committed drawing is not that gate and does not want its exclusion.
/// </remarks>
public sealed class ExampleArchitectureDocTests
{
    /// <summary>
    ///     The four committed drawings, each with the solution its survey fence is captioned from and
    ///     the spec assembly its law fence is captioned from.
    /// </summary>
    private static readonly (string Path, string Solution, string Spec)[] Pinned =
    [
        ("examples/Meridian/ARCHITECTURE.md", "Meridian.slnx", "Meridian.ArchSpec"),
        ("examples/Meridian.Quoting/ARCHITECTURE.md", "Meridian.Quoting.slnx", "Meridian.Quoting.ArchSpec"),
        ("examples/Meridian.Operations/ARCHITECTURE.md", "Meridian.Operations.slnx", "Meridian.Operations.ArchSpec"),
        ("examples/Meridian.Interchange/ARCHITECTURE.md", "Meridian.Interchange.slnx", "Meridian.Interchange.ArchSpec")
    ];

    public static TheoryData<string, string, string> ExampleDocs
    {
        get
        {
            TheoryData<string, string, string> data = new();
            foreach ((string path, string solution, string spec) in Pinned) data.Add(path, solution, spec);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ExampleDocs))]
    public void ExampleArchitectureDoc_CarriesBothFencesAroundHandWrittenProse(
        string relativePath, string solutionFileName, string specName)
    {
        // Arrange
        string committed = RepoRoot.ReadText(relativePath);

        // Act
        string? body = ManagedBlock.ExtractBody(committed);

        // Assert
        TextNormalization.Occurrences(committed, ManagedBlock.BeginMarker)
            .ShouldBe(1, $"{relativePath} must carry exactly one managed block.");
        TextNormalization.Occurrences(committed, ManagedBlock.EndMarker)
            .ShouldBe(1, $"{relativePath} must carry exactly one managed block.");
        body.ShouldNotBeNull($"{relativePath} carries no managed block.");
        TextNormalization.Occurrences(body, "```mermaid")
            .ShouldBe(2, $"{relativePath} must carry both drawings.");
        body.ShouldContain(
            $"accTitle: Codebase survey: {solutionFileName}",
            $"{relativePath}'s survey fence must be captioned from {solutionFileName}.");
        body.ShouldContain(
            $"accTitle: Architecture law: {specName}",
            $"{relativePath}'s law fence must be captioned from {specName}, not another example's spec.");
        Prose(committed, body)
            .ShouldNotBeNullOrWhiteSpace($"{relativePath} carries no hand-written prose around its block.");
    }

    /// <summary>
    ///     The gate on the survey fence's declared-members default, read straight off the committed
    ///     artifacts. Each example's spec project references the LoadBearing packages it is written
    ///     against, so the workspace loads this repository's projects on every example render; only the
    ///     fence's membership filter keeps them off a page captioned "Projects in this solution". The
    ///     render lines carry no scope flag any more, which is the point — so if that default regressed
    ///     there would be nothing else between the passengers and the four committed drawings.
    ///     <para>
    ///         The second half closes the same hole from the other side: whatever narrows the fence could
    ///         narrow it too far and drop a project the example genuinely owns, and reading the drawing
    ///         alone cannot say which it did. The solution file can.
    ///     </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ExampleDocs))]
    public void ExampleArchitectureDoc_SurveysThisExamplesProjectsAndNoOthers(
        string relativePath, string solutionFileName, string specName)
    {
        // Arrange
        string absolutePath = RepoRoot.Absolute(relativePath);
        string body = ManagedBlock.ExtractBody(File.ReadAllText(absolutePath))!;
        string solutionPath = Path.Combine(Path.GetDirectoryName(absolutePath)!, solutionFileName);

        // Act
        List<string> missing = DeclaredProjectNames(solutionPath)
            .Where(name => !body.Contains($"[\"{name}\"]", StringComparison.Ordinal))
            .ToList();

        // Assert
        body.ShouldNotContain(
            "Zphil.LoadBearing",
            $"{relativePath} draws this repository's own projects, which {solutionFileName} does not "
            + "declare; the survey fence's declared-members default has regressed.");
        missing.ShouldReportNothing(
            $"{relativePath} omits project(s) {solutionFileName} declares, so the survey fence narrowed "
            + $"further than declared membership (spec {specName})");
    }

    /// <summary>
    ///     The inventory, mirroring <c>DocHygieneTests.EveryTrackedReaderDoc_IsInsideTheBudgetGate</c>:
    ///     a fifth example cannot ship a drawing that no case above covers, and so cannot ship one whose
    ///     currency nothing gates.
    /// </summary>
    [Fact]
    public void EveryTrackedExampleArchitectureDoc_IsOneOfTheFourPinned()
    {
        // Arrange
        List<string> pinned = Pinned.Select(static entry => entry.Path)
            .ToList();

        // Act
        List<string> uncovered = TrackedFiles.All
            .Where(static path => path.StartsWith("examples/", StringComparison.Ordinal))
            .Where(static path => Path.GetFileName(path) == "ARCHITECTURE.md")
            .Where(path => !pinned.Contains(path))
            .ToList();

        // Assert
        uncovered.ShouldReportNothing("Example drawing(s) outside the pinned set");
    }

    // The project names an .slnx declares, by csproj basename, which is what the survey labels a node
    // with. Read from the solution rather than listed here, so a new project in an example reds this
    // rather than going silently undrawn.
    private static IReadOnlyList<string> DeclaredProjectNames(string solutionPath)
    {
        return XDocument.Load(solutionPath)
            .Descendants("Project")
            .Select(static element => (string?)element.Attribute("Path"))
            .Where(static path => path is not null)
            .Select(static path => Path.GetFileNameWithoutExtension(path!.Replace('\\', '/')))
            .ToList();
    }

    // Everything outside the managed block: the hand-written preamble and the trailing prose.
    private static string Prose(string committed, string body)
    {
        return committed.Replace(body, string.Empty, StringComparison.Ordinal)
            .Replace(ManagedBlock.BeginMarker, string.Empty, StringComparison.Ordinal)
            .Replace(ManagedBlock.EndMarker, string.Empty, StringComparison.Ordinal);
    }
}
