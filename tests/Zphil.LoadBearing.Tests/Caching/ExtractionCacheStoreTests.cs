using System.Text.Json.Nodes;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Tests.Caching;

/// <summary>
///     Unit-tier tests for <see cref="ExtractionCacheStore" /> over hand-rolled synthetic project trees in
///     temp directories — no MSBuild, no fixture solutions. The store never parses the solution or runs a
///     build, so fake <c>.sln</c>/<c>.csproj</c>/<c>.cs</c>/<c>project.assets.json</c> files are enough to
///     exercise every validation path: version gating, the structural/document sweep with the stat fast
///     path, the cone scan, Merkle dirty propagation, torn/garbled reads, and write discipline.
/// </summary>
public sealed class ExtractionCacheStoreTests
{
    [Fact]
    public void ReadAndValidate_SchemaVersionMismatch_ReturnsMiss()
    {
        // Arrange
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution)).ShouldBeTrue();

        // Act — a cache written by a future schema version is unusable.
        solution.MutateCacheJson(root => root["SchemaVersion"] = 999);

        // Assert
        store.ReadAndValidate().Outcome.ShouldBe(CacheOutcome.Miss);
    }

    [Fact]
    public void ReadAndValidate_PriorSchemaVersion13_ReturnsMiss()
    {
        // Arrange — a v13 cache predates the catch edge's swallowing-site subset (schema bumped 13→14): its catch
        // edges carry no SwallowingSites, so every one would replay with a null subset and the rethrow fact would
        // read wrong on a hit. It must degrade cleanly instead.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution)).ShouldBeTrue();

        // Act — downgrade the recorded schema to the immediately-prior version.
        solution.MutateCacheJson(root => root["SchemaVersion"] = 13);

        // Assert — an old-schema cache degrades cleanly to a rebuild, never a wrong answer.
        store.ReadAndValidate().Outcome.ShouldBe(CacheOutcome.Miss);
    }

    [Fact]
    public void ReadAndValidate_PriorSchemaSpecResolutionShape_IsACleanMissNotADeserializationCrash()
    {
        // The manifest is parsed before its schema version is checked, so a genuinely v9-shaped spec record —
        // a scalar `ExcludeProjectName` where v10 expects an `ExcludeProjectNames` list — must not throw its
        // way out of a read. The cache is disposable derived data: every failure is a miss.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        var extraction = new ExtractionResult(
            solution.Projects.Select(p => new CodebaseFragment(p.ProjectName, p.ProjectReferences, [], [], [], [], [], [], [], [], [], [])).ToList(),
            [new SpecResolutionRecord("", "A", ["A"], "/out/A.dll")],
            []);
        store.Write(store.CaptureFingerprint(solution.Projects), extraction).ShouldBeTrue();

        solution.MutateCacheJson(root =>
        {
            root["SchemaVersion"] = 9;
            var record = (JsonObject)root["SpecResolutions"]![0]!;
            record.Remove("ExcludeProjectNames");
            record["ExcludeProjectName"] = "A";
        });

        store.ReadAndValidate().Outcome.ShouldBe(CacheOutcome.Miss);
    }

    [Fact]
    public void ReadAndValidate_ExcludedStrayInCone_StillHits()
    {
        // Arrange — a *.cs on disk in the project cone but not among the project's documents (a <Compile
        // Remove> file). This is the cone-stray defect: CaptureFingerprint recorded adds=[] while validation's cone
        // scan reads the stray as an add, so the project's content key never matched and it validated dirty
        // forever. Capture now computes adds the same way, so the stray lands in both and cancels.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.AddStrayFile("A", Path.Combine("Snippets", "Excluded.cs"), "class Excluded {}");
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution)).ShouldBeTrue();

        // Act — steady-state revalidation with the stray untouched.
        CacheReadResult result = store.ReadAndValidate();

        // Assert — a clean hit, not a perpetual Partial.
        result.Outcome.ShouldBe(CacheOutcome.Hit);
    }

    [Fact]
    public void ReadAndValidate_ToolVersionMismatch_ReturnsMiss()
    {
        // Arrange
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution)).ShouldBeTrue();

        // Act — a cache from a different tool build (commit) is discarded.
        solution.MutateCacheJson(root => root["ToolVersion"] = "0.0.0-not-this-build");

        // Assert
        store.ReadAndValidate().Outcome.ShouldBe(CacheOutcome.Miss);
    }

    [Fact]
    public void ReadAndValidate_GarbledJson_ReturnsMissWithoutThrowing()
    {
        // Arrange
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        ExtractionCacheStore store = solution.NewStore();
        string path = solution.CacheFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not: valid json ]");

        // Act
        CacheReadResult result = Should.NotThrow(() => store.ReadAndValidate());

        // Assert
        result.Outcome.ShouldBe(CacheOutcome.Miss);
    }

    [Fact]
    public void ReadAndValidate_TornWriteTruncatesFile_ReturnsMiss()
    {
        // Arrange — a valid cache, then truncated to half its bytes (a torn write).
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution)).ShouldBeTrue();

        byte[] bytes = File.ReadAllBytes(solution.CacheFilePath);
        File.WriteAllBytes(solution.CacheFilePath, bytes[..(bytes.Length / 2)]);

        // Act + Assert
        Should.NotThrow(() => store.ReadAndValidate()).Outcome.ShouldBe(CacheOutcome.Miss);
    }

    /// <summary>
    ///     One case per name in <see cref="FileStamping.StructuralProbeFileNames" />, read off the array
    ///     itself rather than restated here, so a probe added later arrives with its coverage.
    /// </summary>
    public static TheoryData<string> StructuralProbeCases
    {
        get
        {
            var cases = new TheoryData<string>();
            foreach (string probeFileName in FileStamping.StructuralProbeFileNames) cases.Add(probeFileName);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(StructuralProbeCases))]
    public void ReadAndValidate_NewStructuralProbeFileAppearsInAncestor_ReturnsMiss(string probeFileName)
    {
        // Arrange — the probe chain records this solution-directory file as absent at capture.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution)).ShouldBeTrue();
        store.ReadAndValidate().Outcome.ShouldBe(CacheOutcome.Hit); // baseline: clean before the file appears

        // Act — the recorded-absent probe file appears. The store stats and hashes rather than parsing, so
        // the bytes carry no meaning here; the file existing is the whole signal.
        File.WriteAllText(Path.Combine(solution.Root, probeFileName), "probe\n");

        // Assert — an existence flip on a structural probe is a full miss.
        store.ReadAndValidate().Outcome.ShouldBe(CacheOutcome.Miss);
    }

    [Fact]
    public void ReadAndValidate_MtimeBumpedSameContent_RehashesOnceThenStatFastPath()
    {
        // Arrange — backdate so every stamp is captured promoted, then write.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"), ("B.cs", "class B {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution)).ShouldBeTrue();

        // A clean tree is a pure-stat hit: zero content reads even on the first validation.
        long baseline = store.ContentHashCount;
        store.ReadAndValidate().Outcome.ShouldBe(CacheOutcome.Hit);
        (store.ContentHashCount - baseline).ShouldBe(0);

        // Act 1 — bump one document's mtime to a different past instant without changing its bytes.
        File.SetLastWriteTimeUtc(solution.PathOf("A", "A.cs"), DateTime.UtcNow.AddHours(-1));
        long beforeBumpRead = store.ContentHashCount;
        CacheReadResult afterBump = store.ReadAndValidate();

        // Assert 1 — exactly one re-hash (the bumped file), and still a hit (content unchanged).
        afterBump.Outcome.ShouldBe(CacheOutcome.Hit);
        (store.ContentHashCount - beforeBumpRead).ShouldBe(1);

        // Act 2 — validate again with nothing touched.
        long beforeSecond = store.ContentHashCount;
        CacheReadResult second = store.ReadAndValidate();

        // Assert 2 — the hit rewrote a promoted stamp, so the next validation hashes nothing.
        second.Outcome.ShouldBe(CacheOutcome.Hit);
        (store.ContentHashCount - beforeSecond).ShouldBe(0);
    }

    [Fact]
    public void ReadAndValidate_DependencyContentChanged_DirtySetIncludesMerkleDependents()
    {
        // Arrange — A <- B <- C (B references A, C references B); D is unrelated.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.AddProject("B", ["A"], ("B.cs", "class B {}"));
        solution.AddProject("C", ["B"], ("C.cs", "class C {}"));
        solution.AddProject("D", [], ("D.cs", "class D {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution)).ShouldBeTrue();

        // Act — change A's content; the Merkle keys carry the change to every dependent.
        File.WriteAllText(solution.PathOf("A", "A.cs"), "class A { int x; }");
        File.SetLastWriteTimeUtc(solution.PathOf("A", "A.cs"), DateTime.UtcNow.AddHours(-1));
        CacheReadResult result = store.ReadAndValidate();

        // Assert — {A, B, C} dirty (D clean); the reusable fragments are exactly the clean projects'.
        result.Outcome.ShouldBe(CacheOutcome.Partial);
        result.DirtyProjects.ShouldBe(["A", "B", "C"], true);
        result.ReusableFragments.Select(f => f.ProjectName).ShouldBe(["D"]);
    }

    [Fact]
    public void Write_FileMutatedBetweenFingerprintAndWrite_SkipsWrite()
    {
        // Arrange — capture the fingerprint, then edit a document before writing (a mid-run edit).
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        CacheFingerprint fingerprint = store.CaptureFingerprint(solution.Projects);

        File.WriteAllText(solution.PathOf("A", "A.cs"), "class A { int y; }");
        File.SetLastWriteTimeUtc(solution.PathOf("A", "A.cs"), DateTime.UtcNow);

        // Act
        bool wrote = store.Write(fingerprint, TrivialExtraction(solution));

        // Assert — the write is skipped, and no cache file is left behind to poison a later run.
        wrote.ShouldBeFalse();
        File.Exists(solution.CacheFilePath).ShouldBeFalse();
    }

    [Fact]
    public void Write_ValidCacheOverwrittenByNewWrite_ReadsBackTheNewContent()
    {
        // Arrange
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();

        store.Write(store.CaptureFingerprint(solution.Projects), OneFragment(solution, "first")).ShouldBeTrue();
        store.ReadAndValidate().Diagnostics.ShouldBe(["first"]);

        // Act — a second atomic write fully replaces the file.
        store.Write(store.CaptureFingerprint(solution.Projects), OneFragment(solution, "second")).ShouldBeTrue();
        CacheReadResult result = store.ReadAndValidate();

        // Assert — the new content, whole (no partial state from the overwrite).
        result.Outcome.ShouldBe(CacheOutcome.Hit);
        result.Diagnostics.ShouldBe(["second"]);
    }

    [Fact]
    public void ReadAndValidate_CleanHit_ReplaysAllFragmentsAndRecordedSidecars()
    {
        // Arrange
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.AddProject("B", ["A"], ("B.cs", "class B {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        SpecResolutionRecord[] specs = [new("", "A", ["A", "PrivatePack"], "/out/A.dll")];
        var extraction = new ExtractionResult(
            solution.Projects.Select(p => new CodebaseFragment(p.ProjectName, p.ProjectReferences, [], [], [], [], [], [], [], [], [], [])).ToList(),
            specs,
            ["load-diag-1", "load-diag-2"]);
        store.Write(store.CaptureFingerprint(solution.Projects), extraction).ShouldBeTrue();

        // Act
        CacheReadResult result = store.ReadAndValidate();

        // Assert — a full hit returns every fragment plus the recorded spec resolutions and diagnostics.
        result.Outcome.ShouldBe(CacheOutcome.Hit);
        result.ReusableFragments.Select(f => f.ProjectName).ShouldBe(["A", "B"], true);
        result.DirtyProjects.ShouldBeEmpty();
        // Field-by-field: the record carries a collection, so its synthesized equality compares that member
        // by reference and a round-tripped list can never equal the written one.
        SpecResolutionRecord replayed = result.SpecResolutions.ShouldHaveSingleItem();
        replayed.NormalizedSpecArgument.ShouldBe("");
        replayed.SpecProjectName.ShouldBe("A");
        replayed.ExcludeProjectNames.ShouldBe(["A", "PrivatePack"]);
        replayed.OutputFilePath.ShouldBe("/out/A.dll");
        result.Diagnostics.ShouldBe(["load-diag-1", "load-diag-2"]);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────

    private static ExtractionResult TrivialExtraction(SyntheticSolution solution)
    {
        var fragments = solution.Projects
            .Select(p => new CodebaseFragment(p.ProjectName, p.ProjectReferences, [], [], [], [], [], [], [], [], [], []))
            .ToList();
        return new ExtractionResult(fragments, [], ["diag"]);
    }

    private static ExtractionResult OneFragment(SyntheticSolution solution, string diagnostic)
    {
        var fragments = solution.Projects
            .Select(p => new CodebaseFragment(p.ProjectName, p.ProjectReferences, [], [], [], [], [], [], [], [], [], []))
            .ToList();
        return new ExtractionResult(fragments, [], [diagnostic]);
    }

    /// <summary>
    ///     A throwaway synthetic solution tree under <c>%TEMP%</c>: a fake <c>.sln</c>, and per project a
    ///     directory with a fake <c>.csproj</c>, an <c>obj/project.assets.json</c>, and source files. The
    ///     cache root is a sibling directory, so the store's own writes never collide with the fake inputs.
    /// </summary>
    private sealed class SyntheticSolution : IDisposable
    {
        private readonly List<ProjectInputs> projects = [];

        public SyntheticSolution()
        {
            Root = Path.Combine(Path.GetTempPath(), "lb-cache-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            CacheRoot = Path.Combine(Root, "cache");
            SolutionPath = Path.Combine(Root, "App.sln");
            File.WriteAllText(SolutionPath, "Microsoft Visual Studio Solution File\n");
        }

        public string Root { get; }

        public string CacheRoot { get; }

        public string SolutionPath { get; }

        public string CacheFilePath => CacheLocations.CacheFilePath(SolutionPath, CacheRoot);

        public IReadOnlyList<ProjectInputs> Projects => projects;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // best-effort cleanup of the temp tree
            }
        }

        public void AddProject(string name, IReadOnlyList<string> references, params (string File, string Content)[] documents)
        {
            string directory = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.Combine(directory, "obj"));
            string csproj = Path.Combine(directory, $"{name}.csproj");
            File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
            File.WriteAllText(Path.Combine(directory, "obj", "project.assets.json"), "{}\n");

            var documentPaths = new List<string>();
            foreach ((string file, string content) in documents)
            {
                string documentPath = Path.Combine(directory, file);
                File.WriteAllText(documentPath, content);
                documentPaths.Add(documentPath);
            }

            var inputs = new ProjectInputs(name, csproj, directory, references, documentPaths);
            projects.Add(inputs);
        }

        // Writes a *.cs into an existing project's directory WITHOUT recording it as a document — the
        // on-disk-but-excluded stray (a <Compile Remove> file) the cone scan sees but the compiler does not.
        public void AddStrayFile(string project, string relativePath, string content)
        {
            string path = Path.Combine(Root, project, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public string PathOf(string project, string file)
        {
            return Path.Combine(Root, project, file);
        }

        public ExtractionCacheStore NewStore()
        {
            return new ExtractionCacheStore(SolutionPath, CacheRoot);
        }

        // Set every input file's mtime well into the past so a capture stamps it promoted — the precondition
        // for the stat fast path (the persisted-cache analog of WorkspaceSession's BackdateAllDocuments).
        public void BackdateAll()
        {
            DateTime wellPast = DateTime.UtcNow.AddDays(-1);
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                if (file.StartsWith(CacheRoot, PathComparison.Comparison)) continue;
                try
                {
                    File.SetLastWriteTimeUtc(file, wellPast);
                }
                catch (IOException)
                {
                    // best-effort: a file we cannot re-stamp simply re-reads once, which the tests tolerate
                }
            }
        }

        public void MutateCacheJson(Action<JsonObject> mutate)
        {
            var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CacheFilePath))!;
            mutate(root);
            File.WriteAllText(CacheFilePath, root.ToJsonString());
        }
    }
}
