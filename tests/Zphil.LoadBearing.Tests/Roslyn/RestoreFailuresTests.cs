using System.Text;
using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Solutions;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     <see cref="RestoreFailures.Detect" /> over a pure <see cref="AdhocWorkspace" /> graph plus real
///     <c>project.assets.json</c> files on disk — the second half of the fail-closed gate's input, beside
///     <see cref="ProjectLoadFailures" />.
/// </summary>
/// <remarks>
///     <para>
///         The JSON shapes here are the ones real restores were measured to write, not ones invented to fit
///         the code: a clean assets file carries <em>no</em> <c>logs</c> key at all, and a failed one carries
///         the failure as a <c>{ "code", "level", "message" }</c> entry beside an empty <c>libraries</c>
///         section. Both <c>code</c> and <c>level</c> are invariant fields, which is the whole reason this
///         predicate can be language-independent where a message match could not.
///     </para>
///     <para>
///         <b>The second arm reads a file that is not there.</b> No assets file at all means the restore never
///         ran, and whether that is a fact about the project depends on whether it is one that would have
///         written the file — so the rows below that leave the <c>obj/</c> directory out write a real
///         <c>.csproj</c> instead, in the shape whose reading decides. That reading is
///         <see cref="SdkStyleProject" />'s, measured over the whole corpus by
///         <see cref="SdkStyleProjectTests" />; what is pinned here is that this predicate consults it, and
///         only where there is no assets file to read.
///     </para>
///     <para>
///         The end-to-end fact — a rule that reds on the restored tree does not pass on the same tree with
///         the restore broken — belongs to <c>RestoreFailureSilentEdgeE2ETests</c>, which pays a real
///         <c>dotnet restore</c> for it. What is pinned here is the predicate itself, so a change to it reds
///         in milliseconds rather than only in the serial lane.
///     </para>
///     <para>
///         <b>The limit this file inherits.</b> <c>CompilationOutputInfo</c> has no public constructor, so an
///         <see cref="AdhocWorkspace" /> project's intermediate assembly path is always null — the limit
///         <see cref="AdhocSolution" /> documents. Every project below therefore reaches
///         <see cref="IntermediateOutputTree.AssetsPathsOf" /> with one of its two evaluated paths unknown,
///         which is the degraded case that yields the default <c>obj/</c> location alone. The derived
///         candidates for the artifacts and redirected-intermediate layouts are
///         <c>IntermediateOutputTreeTests</c>' subject; what this file can prove is that the list is consulted
///         and that a file found on it decides.
///     </para>
/// </remarks>
public sealed class RestoreFailuresTests
{
    // A failed restore, as measured: an empty libraries section and the NuGet error in the logs array.
    private const string FailedRestoreAssets =
        """
        {
          "version": 3,
          "libraries": {},
          "logs": [
            {
              "code": "NU1301",
              "level": "Error",
              "message": "Unable to load the service index for source https://nuget.fieldtest.invalid/v3/index.json."
            }
          ]
        }
        """;

    // A clean restore, as measured: no logs key at all, which is why absence must read as success.
    private const string CleanAssets =
        """
        {
          "version": 3,
          "libraries": { "Newtonsoft.Json/13.0.3": { "type": "package" } },
          "projectFileDependencyGroups": { "net10.0": [ "Newtonsoft.Json >= 13.0.3" ] }
        }
        """;

    // The project a restore would have written an assets file for.
    private const string SdkStyleProjectXml =
        """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup>
        </Project>
        """;

    // The one it would not: a non-SDK-style .NET Framework project in the 2003 namespace, whose absent assets
    // file is the state it lives in.
    private const string LegacyProject =
        """
        <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
            <PropertyGroup>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
            </PropertyGroup>
        </Project>
        """;

    [Fact]
    public void Detect_AssetsFileRecordingARestoreError_ReportsTheProject()
    {
        // The measured case: the restore exited 1 with NU1301, the project still loaded completely, and every
        // package edge it declares is missing from the model.
        using TempDirectory temp = TestTempRoot.Fresh("restore-failed");
        string csproj = WriteAssets(temp, "Core", FailedRestoreAssets);
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBe([csproj]);
    }

    [Fact]
    public void Detect_AssetsFileWithNoLogsKey_IsNotAFailure()
    {
        // The negative control, and the shape almost every project on disk has: a clean restore writes no
        // logs key, so "no key" must read as success rather than as something missing.
        using TempDirectory temp = TestTempRoot.Fresh("restore-clean");
        string csproj = WriteAssets(temp, "Core", CleanAssets);
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_AssetsFileWithOnlyWarningLogs_IsNotAFailure()
    {
        // NU1510 is the pruning advisory that refused a solution whose rules all passed under the old
        // message-matching gate. It is level Warning, and level is what decides here — so it cannot.
        using TempDirectory temp = TestTempRoot.Fresh("restore-warned");
        string csproj = WriteAssets(
            temp,
            "Core",
            Logs("""{ "code": "NU1510", "level": "Warning", "message": "PackageReference will not be pruned." }"""));
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_AssetsFileWithAnEmptyLogsArray_IsNotAFailure()
    {
        using TempDirectory temp = TestTempRoot.Fresh("restore-empty-logs");
        string csproj = WriteAssets(temp, "Core", """{ "version": 3, "logs": [] }""");
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_NonSdkStyleProjectWithNoAssetsFile_IsNotAFailure()
    {
        // Decision 3, pinned: absence asserts nothing here. A non-SDK-style .NET Framework project never
        // writes an assets file, and this product explicitly supports those — "absent ⇒ failed" would refuse
        // exactly the legacy solutions it was built for.
        using TempDirectory temp = TestTempRoot.Fresh("restore-absent-legacy");
        string csproj = WriteProject(temp, "Classic", LegacyProject);
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_SdkStyleProjectWithNoAssetsFile_ReportsTheProject()
    {
        // The never-restored case. This project loads completely — full document and reference sets, both
        // output paths, no workspace diagnostic — while missing every package edge exactly as a failed
        // restore leaves it, so nothing but the absent file says so. An SDK-style project writes one on every
        // restore, which is what makes the absence a fact rather than a shrug.
        using TempDirectory temp = TestTempRoot.Fresh("restore-absent-sdk");
        string csproj = WriteProject(temp, "Core", SdkStyleProjectXml);
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBe([csproj]);
    }

    [Fact]
    public void Detect_MalformedProjectFileWithNoAssetsFile_IsNotAFailure()
    {
        // A csproj we could not read says nothing about whether it would have written an assets file, and
        // silence is the safe direction for the same reason it is on the assets read: a false negative leaves
        // a partial model undetected, a false positive refuses a healthy solution.
        using TempDirectory temp = TestTempRoot.Fresh("restore-absent-malformed");
        string csproj = WriteProject(temp, "Core", """<Project Sdk="Microsoft.NET.Sdk">""");
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_SdkStyleProjectWithNoAssetsFileTheLoadPredicateAlreadyBlamed_IsNotReportedTwice()
    {
        // The subtraction has to cover the second arm too, and it is likelier to bite there: a project that
        // failed to load never restored either, so both predicates have something to say about it. The one
        // that says "it never loaded" wins, because it is the more fundamental repair.
        using TempDirectory temp = TestTempRoot.Fresh("restore-absent-already-failed");
        string csproj = WriteProject(temp, "Core", SdkStyleProjectXml);
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [csproj])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_SdkStyleProjectWithACleanAssetsFile_IsNotAFailure()
    {
        // The negative control the second arm needs, and the shape almost every SDK-style project on disk
        // has: being SDK-style is only ever a reason to read the absence of an assets file, never a finding
        // of its own. A file that is there decides on its own contents.
        using TempDirectory temp = TestTempRoot.Fresh("restore-sdk-clean");
        string csproj = WriteAssets(temp, "Core", CleanAssets);
        File.WriteAllText(csproj, SdkStyleProjectXml);
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_MultiTargetFrameworkProjectWithNoAssetsFile_CollapsesToOneCsprojPath()
    {
        // The same collapse the first arm makes, on the arm that reads the csproj rather than the assets
        // file: several Projects behind one file, one path to go and fix, read once.
        using TempDirectory temp = TestTempRoot.Fresh("restore-absent-multi-tfm");
        string csproj = WriteProject(temp, "Core", SdkStyleProjectXml);
        using var workspace = new AdhocWorkspace();
        Solution solution = AdhocSolution.Of(
            workspace, AdhocSolution.Loaded("Core(net10.0)", csproj), AdhocSolution.Loaded("Core(netstandard2.0)", csproj));

        RestoreFailures.Detect(solution, [])
            .ShouldBe([csproj]);
    }

    [Fact]
    public void Detect_MalformedAssetsFile_IsNotAFailure()
    {
        // A file we could not read says nothing about the restore, and silence is the only safe answer: the
        // alternative is refusing a healthy solution, which is the disease the structural gate cured.
        using TempDirectory temp = TestTempRoot.Fresh("restore-malformed");
        string csproj = WriteAssets(temp, "Core", "{ not json at all");
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_AssetsFileWhoseLogsAreNotTheExpectedShape_IsNotAFailure()
    {
        // Well-formed JSON, wrong node kinds throughout — a logs array of strings, and an entry whose level is
        // a number. Every one degrades to "not failed" rather than throwing a second exception mid-load.
        using TempDirectory temp = TestTempRoot.Fresh("restore-odd-shape");
        string strings = WriteAssets(temp, "Strings", """{ "logs": [ "NU1301" ] }""");
        string numbers = WriteAssets(temp, "Numbers", Logs("""{ "code": "NU1301", "level": 2 }"""));
        string objectLogs = WriteAssets(temp, "Objects", """{ "logs": { "code": "NU1301", "level": "Error" } }""");
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, strings, numbers, objectLogs), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_AssetsFileWithAUtf8Bom_StillReportsTheProject()
    {
        // Every assets file a real restore writes carries a UTF-8 BOM — measured on two restored field beds,
        // both of which needed a BOM-aware reader. The rest of this class writes them without one, so without
        // this row the shape production actually meets would be pinned only incidentally, by an end-to-end
        // test that pays a live restore for it.
        using TempDirectory temp = TestTempRoot.Fresh("restore-bom");
        string csproj = WriteAssets(temp, "Core", FailedRestoreAssets, true);
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBe([csproj]);
    }

    [Fact]
    public void Detect_ErrorLogWithNoCode_StillReportsTheProject()
    {
        // level is what decides; code only ever exempts. An entry with no code cannot be an audit entry, so
        // the carve-out must not become a requirement to carry one — a schema that ever dropped the field
        // would otherwise silently disable the whole predicate.
        using TempDirectory temp = TestTempRoot.Fresh("restore-codeless");
        string csproj = WriteAssets(temp, "Core", Logs("""{ "level": "Error", "message": "Restore failed." }"""));
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBe([csproj]);
    }

    [Fact]
    public void Detect_AssetsFileWhoseRootIsNotAnObject_IsNotAFailure()
    {
        // Well-formed JSON that is not an assets file at all. Asking a JSON array for a property throws, so
        // the kind is checked before the read — a degradation, not an exception raised mid-load.
        using TempDirectory temp = TestTempRoot.Fresh("restore-array-root");
        string csproj = WriteAssets(temp, "Core", """[ { "level": "Error" } ]""");
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_AuditCodedError_IsNotAFailure()
    {
        // The one carve-out. NU19xx is level Warning by default, but TreatWarningsAsErrors promotes it — and an
        // advisory published this morning is an external, time-varying input that says nothing about how this
        // codebase is built, while resolution succeeded and the model is complete. Refusing on one is issue #19.
        using TempDirectory temp = TestTempRoot.Fresh("restore-audit");
        string csproj = WriteAssets(
            temp,
            "Core",
            Logs(
                """
                { "code": "NU1903", "level": "Error",
                  "message": "Package 'X' 1.0.0 has a known high severity vulnerability" }
                """));
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_AuditCodedErrorBesideARealOne_StillReportsTheProject()
    {
        // The carve-out drops entries, never projects: an advisory riding along with a genuine resolution
        // failure must not launder it.
        using TempDirectory temp = TestTempRoot.Fresh("restore-audit-and-real");
        string csproj = WriteAssets(
            temp,
            "Core",
            Logs(
                """{ "code": "NU1903", "level": "Error", "message": "known high severity vulnerability" }""",
                """{ "code": "NU1301", "level": "Error", "message": "Unable to load the service index." }"""));
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [])
            .ShouldBe([csproj]);
    }

    [Fact]
    public void Detect_MultiTargetFrameworkProject_CollapsesToOneCsprojPath()
    {
        // One csproj behind several Projects sharing one assets file: the reader has one file to go and fix,
        // so the answer names it once rather than once per framework.
        using TempDirectory temp = TestTempRoot.Fresh("restore-multi-tfm");
        string csproj = WriteAssets(temp, "Core", FailedRestoreAssets);
        using var workspace = new AdhocWorkspace();
        Solution solution = AdhocSolution.Of(
            workspace, AdhocSolution.Loaded("Core(net10.0)", csproj), AdhocSolution.Loaded("Core(netstandard2.0)", csproj));

        RestoreFailures.Detect(solution, [])
            .ShouldBe([csproj]);
    }

    [Fact]
    public void Detect_ProjectTheLoadPredicateAlreadyBlamed_IsNotReportedTwice()
    {
        // A project caught by ProjectLoadFailures' loaded-but-empty arm carries neither output path, so the
        // candidate list degrades to the default location — which may exist and carry an error from a restore
        // that ran before the project stopped evaluating. One project must not appear in two lists.
        using TempDirectory temp = TestTempRoot.Fresh("restore-already-failed");
        string csproj = WriteAssets(temp, "Core", FailedRestoreAssets);
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, csproj), [csproj])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_SeveralFailedProjects_AreOrdinalSorted()
    {
        // The shape every sibling list has, so a refusal reads the same however the solution happened to order
        // its members.
        using TempDirectory temp = TestTempRoot.Fresh("restore-ordering");
        string zed = WriteAssets(temp, "Zed", FailedRestoreAssets);
        string core = WriteAssets(temp, "Core", FailedRestoreAssets);
        using var workspace = new AdhocWorkspace();

        RestoreFailures.Detect(SolutionOf(workspace, zed, core), [])
            .ShouldBe([core, zed]);
    }

    [Fact]
    public void Detect_ProjectWithNoFilePath_IsSkipped()
    {
        // Nothing to name and nowhere to look. The answer a refusal gives is a set of paths, so a project
        // without one cannot join it.
        using var workspace = new AdhocWorkspace();
        workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, "Pathless", "Pathless", LanguageNames.CSharp));

        RestoreFailures.Detect(workspace.CurrentSolution, [])
            .ShouldBeEmpty();
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    // An assets file at the default location for a project named after the directory, returning the csproj
    // path the answer is expressed in. The obj/ spelling is the harness's, deliberately: production derives
    // it from IntermediateOutputTree, and a test that also derived it could not catch the derivation moving.
    private static string WriteAssets(
        TempDirectory temp, string projectName, string assetsJson, bool withBom = false)
    {
        string objDirectory = temp.PathOf(projectName, "obj");
        Directory.CreateDirectory(objDirectory);
        File.WriteAllText(
            Path.Combine(objDirectory, "project.assets.json"), assetsJson, new UTF8Encoding(withBom));
        return temp.PathOf(projectName, projectName + ".csproj");
    }

    // A project file and no obj/ directory at all — the never-restored shape, where the csproj is the only
    // thing there is to read.
    private static string WriteProject(TempDirectory temp, string projectName, string projectXml)
    {
        Directory.CreateDirectory(temp.PathOf(projectName));
        string csproj = temp.PathOf(projectName, projectName + ".csproj");
        File.WriteAllText(csproj, projectXml);
        return csproj;
    }

    private static string Logs(params string[] entries)
    {
        return $$"""{ "version": 3, "libraries": {}, "logs": [ {{string.Join(",", entries)}} ] }""";
    }

    // Every project loaded completely — which is the whole point: a failed restore leaves the project
    // carrying its evaluated output path, so ProjectLoadFailures sees nothing wrong with it.
    private static Solution SolutionOf(AdhocWorkspace workspace, params string[] csprojPaths)
    {
        return AdhocSolution.Of(
            workspace,
            [.. csprojPaths.Select(path => AdhocSolution.Loaded(Path.GetFileNameWithoutExtension(path), path))]);
    }
}
