using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     The measurement behind <see cref="SdkStyleProject.IsSdkStyle" />, taken before anything was allowed to
///     gate on it: whether an SDK-style project can be told from a non-SDK-style one reliably enough that a
///     missing <c>project.assets.json</c> may be blamed on the first and never on the second.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why a measurement rather than a description.</b> The predicate on its own only says what the XML
///         contains. The claim it is used for is larger —
///         <em>SDK-style projects write an assets file when they restore, and legacy ones never do</em>
///         — and nothing about reading an attribute establishes
///         that. So the corpus below pairs each verdict with the file it predicts: every project under the
///         three roots it scans, classified, and beside each one whether an assets file is actually on disk at
///         a location <see cref="IntermediateOutputTree.AssetsPathsOf" /> stamps. Eighteen projects, and the
///         only rows where the two columns come apart are the three beds deliberately left unrestored — which
///         is the whole state the gate exists to catch — plus the two of the output-layout bed, which is
///         restored and built only inside the temp copy that drives it.
///     </para>
///     <para>
///         <b>Why the corpus is real files rather than only written ones.</b> The written shapes fix the
///         boundaries — a comment mentioning an SDK, malformed XML, a legacy project that happens to declare a
///         property called <c>Sdk</c> — but they are written by the same reading of MSBuild that wrote the
///         predicate, so agreement between them proves only self-consistency. The real corpus is the part that
///         could have refuted it, and two of its files are the awkward ones on purpose:
///         <c>FieldMini.Core.csproj</c> and <c>BrokenApp.Web.csproj</c> carry their root element under
///         twenty-nine and forty lines of XML comment.
///     </para>
///     <para>
///         <b>Where the corpus stops.</b> Three roots — the fixture trees as the test output holds them,
///         <c>src/</c> and <c>arch/</c> — and not every project file this repository tracks. Out are the
///         spec-fixture projects under <c>tests/Fixtures/</c> and the test project itself, which are solution
///         members and so would every one read <c>SDK-style, assets file present</c>; and everything under
///         <c>examples/</c>, which no run of this suite restores, so their assets column would report
///         whether somebody had built the examples lately rather than anything about the discriminator. What
///         is licensed here is the gate over the trees the suite loads through <c>MSBuildWorkspace</c> — a
///         legacy or unrestored project appearing in either of those places would not be caught by this
///         table.
///     </para>
/// </remarks>
public sealed class SdkStyleProjectTests
{
    // Every project under the three roots RealProjectFiles scans, hand-written: the name, whether it is
    // SDK-style, and whether a restore has actually left an assets file where the layout in force puts it. A
    // row that moves is either a new fixture (extend this) or the discriminator failing on a real file (stop
    // and restate the limit instead of gating). Order is irrelevant — both sides are sorted before the
    // compare. The roots are not the whole repository; the class remarks say what is out and why.
    private static readonly string[] GroundTruth =
    [
        // The one non-SDK-style project in the corpus: 2003 XML namespace, net48, and no assets file — the
        // shape "absent ⇒ failed" would have refused wholesale.
        "Classic.Billing.csproj: legacy, no assets file",
        // The three beds deliberately left unrestored. SDK-style with no assets file is exactly the state
        // that used to pass check silently, and these are the only rows that show it.
        "BrokenApp.Core.csproj: SDK-style, no assets file",
        "BrokenApp.Web.csproj: SDK-style, no assets file",
        "FieldMini.Core.csproj: SDK-style, no assets file",
        // The output-layout bed. Not a fourth unrestored gate bed: this fixture is restored and really
        // built, but only ever inside the temp copy each arm of Cli/OutputLayoutSpecResolutionE2ETests
        // makes — nothing restores it in the output tree, so the assets column reads absent here.
        "LayoutApp.Core.csproj: SDK-style, no assets file",
        "LayoutApp.Spec.csproj: SDK-style, no assets file",
        // The restored fixture solutions: SDK-style, and the assets file the classification predicts is
        // there. This is the pairing — the predicate earns the right to gate here, not in the XML.
        "MultiTfm.Core.csproj: SDK-style, assets file present",
        "MultiTfm.Web.csproj: SDK-style, assets file present",
        "MyApp.Domain.csproj: SDK-style, assets file present",
        "MyApp.Legacy.Billing.csproj: SDK-style, assets file present",
        "MyApp.Web.csproj: SDK-style, assets file present",
        "SlnxApp.Core.csproj: SDK-style, assets file present",
        // This repo's own projects, which are always restored — the same pairing over files nobody wrote to
        // be a fixture.
        "Zphil.LoadBearing.ArchSpec.csproj: SDK-style, assets file present",
        "Zphil.LoadBearing.Cli.csproj: SDK-style, assets file present",
        "Zphil.LoadBearing.Packs.DotNet.csproj: SDK-style, assets file present",
        "Zphil.LoadBearing.Roslyn.csproj: SDK-style, assets file present",
        "Zphil.LoadBearing.Xunit.csproj: SDK-style, assets file present",
        "Zphil.LoadBearing.csproj: SDK-style, assets file present"
    ];

    [Theory]
    // The three shapes MSBuild accepts as an SDK declaration. The attribute is what every template writes;
    // the element and the import pair are what a project pinning an SDK version has to use.
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup /></Project>""", true)]
    [InlineData("""<Project><Sdk Name="Microsoft.NET.Sdk" Version="10.0.100" /><PropertyGroup /></Project>""", true)]
    [InlineData(
        """<Project><Import Sdk="Microsoft.NET.Sdk" Project="Sdk.props" /><PropertyGroup /></Project>""", true)]
    // An Import may legitimately sit inside an ImportGroup, so that arm reads descendants rather than the
    // root's own children.
    [InlineData(
        """<Project><ImportGroup><Import Sdk="Microsoft.NET.Sdk" Project="Sdk.props" /></ImportGroup></Project>""",
        true)]
    // The namespace is not the discriminator, in either direction: MSBuild accepts an SDK-style project that
    // declares the 2003 namespace anyway, and the attribute is what decides. Reading local names is what
    // makes both of these come out right rather than one being inferred from the other.
    [InlineData(
        """<Project Sdk="Microsoft.NET.Sdk" xmlns="http://schemas.microsoft.com/developer/msbuild/2003" />""",
        true)]
    [InlineData(
        """
        <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><PropertyGroup>
        <TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup></Project>
        """,
        false)]
    // A legacy project need not declare the namespace at all, and one that does not is still legacy.
    [InlineData("""<Project ToolsVersion="15.0" DefaultTargets="Build"><PropertyGroup /></Project>""", false)]
    // The negative controls. "Sdk" inside a comment, inside a property value, and as a property name are all
    // things a legacy project may contain — and reading any of them as a declaration would blame a healthy
    // legacy project for a file it was never going to write.
    [InlineData("""<Project ToolsVersion="15.0"><!-- ported off Sdk="Microsoft.NET.Sdk" --></Project>""", false)]
    [InlineData("""<Project ToolsVersion="15.0"><PropertyGroup><Flavour>Sdk</Flavour></PropertyGroup></Project>""",
        false)]
    [InlineData("""<Project ToolsVersion="15.0"><PropertyGroup><Sdk>Microsoft.NET.Sdk</Sdk></PropertyGroup></Project>""",
        false)]
    // A root element that is not Project is not a project file, whatever it carries.
    [InlineData("""<Package Sdk="Microsoft.NET.Sdk" />""", false)]
    // Malformed XML answers false rather than throwing mid-load: a false negative leaves a partial model
    // undetected, which is the state that already existed, while a false positive refuses a healthy solution.
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk">""", false)]
    [InlineData("not xml at all", false)]
    public void IsSdkStyle_OverTheShapesAProjectFileCanTake_ClassifiesEachOne(string projectXml, bool expected)
    {
        using TempDirectory temp = TestTempRoot.Fresh("sdk-style-shapes");
        string projectPath = Path.Combine(temp.Path, "Shape.csproj");
        File.WriteAllText(projectPath, projectXml);

        SdkStyleProject.IsSdkStyle(projectPath)
            .ShouldBe(expected, projectXml);
    }

    [Fact]
    public void IsSdkStyle_AnEmptyFile_IsNotSdkStyle()
    {
        // A truncated write leaves this, and an empty document has no root at all.
        using TempDirectory temp = TestTempRoot.Fresh("sdk-style-empty");
        string projectPath = Path.Combine(temp.Path, "Empty.csproj");
        File.WriteAllText(projectPath, "");

        SdkStyleProject.IsSdkStyle(projectPath)
            .ShouldBeFalse();
    }

    [Fact]
    public void IsSdkStyle_AFileThatIsNotThere_IsNotSdkStyle()
    {
        // The path a solution declares for a project the tree does not contain. ProjectLoadFailures already
        // blames that project, and this must not answer a second time — nor throw where nothing is caught.
        using TempDirectory temp = TestTempRoot.Fresh("sdk-style-absent");

        SdkStyleProject.IsSdkStyle(Path.Combine(temp.Path, "Gone.csproj"))
            .ShouldBeFalse();
    }

    [Fact]
    public void IsSdkStyle_OverEveryRealProjectFile_MatchesTheGroundTruthAndPredictsTheAssetsFile()
    {
        // The measurement AC #1 asks for. Both columns in one table, because the classification alone would
        // only describe the XML: what licenses the gate is that "SDK-style" and "has an assets file" agree
        // everywhere except where a bed was deliberately left unrestored.
        var measured = RealProjectFiles()
            .Select(Describe)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Case.Sensitive is the suite's default posture anyway; it is spelled out because it is the overload
        // that also takes the message, and the message is what a moved row needs to read.
        measured.ShouldBe(
            GroundTruth.Order(StringComparer.Ordinal),
            Case.Sensitive,
            "The SDK-style discriminator was measured against a hand-written map of every project under "
            + "the three roots this corpus scans. A row that moved is either a fixture that was added or "
            + "removed — extend the map — or the discriminator being wrong about a real file, which is the "
            + "measurement refusing to license the gate in RestoreFailures.");
    }

    [Fact]
    public void Detect_TheLegacyBedWithNoAssetsFile_IsStillNotBlamed()
    {
        // AC #3 at the predicate's own level, over the real ClassicApp tree rather than a written stand-in:
        // the one project in the corpus that is not SDK-style is the one the restore gate must never blame,
        // because it was never going to write the file whose absence is being read.
        string classicBilling = Path.Combine(
            FixturesRoot, "LegacySolutions", "ClassicApp", "Classic.Billing", "Classic.Billing.csproj");
        using var workspace = new AdhocWorkspace();
        Solution solution = AdhocSolution.Of(workspace, AdhocSolution.Loaded("Classic.Billing", classicBilling));

        RestoreFailures.Detect(solution, [])
            .ShouldBeEmpty();
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    // Every project file the corpus covers: the fixture trees as the test output holds them (which is what
    // FixtureRestorer restores, so the assets column reads the same files the suite runs against), plus this
    // repo's own shipping and dogfood projects. These three roots are the whole scan — nothing under
    // tests/Fixtures/ or examples/ is read, which the class remarks state as a limit rather than an omission.
    private static IEnumerable<string> RealProjectFiles()
    {
        string[] roots = [FixturesRoot, RepoRoot.Absolute("src"), RepoRoot.Absolute("arch")];

        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories));
    }

    // One row of the table: the classification, and whether the file it predicts is on disk. The candidate
    // paths come from production's own derivation rather than a spelled obj/, so a layout this corpus does
    // not use could not silently read as "absent".
    private static string Describe(string projectPath)
    {
        bool sdkStyle = SdkStyleProject.IsSdkStyle(projectPath);
        bool assets = IntermediateOutputTree
            .AssetsPathsOf(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, null, null)
            .Any(File.Exists);

        return $"{Path.GetFileName(projectPath)}: {(sdkStyle ? "SDK-style" : "legacy")}, "
               + $"{(assets ? "assets file present" : "no assets file")}";
    }
}
