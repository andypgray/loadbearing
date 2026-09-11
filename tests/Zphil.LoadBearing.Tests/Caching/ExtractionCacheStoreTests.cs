using System.Text.Json.Nodes;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Tests.TestSupport;

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
    /// <summary>
    ///     The recorded schema version is the one thing a read can trust before it has deserialized
    ///     anything, so any version but the current one is a miss — whether it is ahead of this reader or
    ///     behind it. Each case below names the wrong answer that version would give on a hit.
    /// </summary>
    [Theory]
    // Ahead: a cache written by a future schema version was written by a writer that knew fields this
    // reader does not.
    [InlineData(999)]
    // A v13 cache predates the catch edge's swallowing-site subset (schema bumped 13→14): its catch edges
    // carry no SwallowingSites, so every one would replay with a null subset and the rethrow fact would read
    // wrong on a hit.
    [InlineData(13)]
    // A v16 cache predates FailedProjects (schema bumped 16→17): it records which diagnostics the load
    // produced but not which projects failed, so a hit would deserialize an empty list and answer green on
    // the one solution shape check exists to refuse. That is the single worst wrong answer this cache could
    // give, so the version it was written under has to be enough to reject it.
    [InlineData(16)]
    // A v18 cache predates RestoreFailedProjects (schema bumped 18→19): it records which projects failed to
    // load but not which projects' NuGet packages were missing, so a hit would deserialize an empty list and
    // answer green on a model missing every package edge — the exact silent pass this field was added to
    // close. The version it was written under has to be enough to reject it.
    [InlineData(18)]
    // A v24 cache predates the member mutability facts (schema bumped 24→25): its members carry no setter or
    // field-writability flags, so on a hit every property would replay as get-only and every field as
    // writable — a get-only rule would pass vacuously over a codebase full of settable properties, and a
    // readonly rule would red every field it saw.
    [InlineData(24)]
    // A v25 cache predates the evaluated artifact facts (schema bumped 25→26): its fragments carry no
    // declared frameworks, package references, packability or lock policy, so on a hit every project would
    // replay as one nothing was evaluated for — a rule about what a project targets or ships would find no
    // subject at all and pass over a solution it has never read.
    [InlineData(25)]
    // A v26 cache predates the shape on a document stamp (schema bumped 26→27). This one would in fact
    // deserialize — the slot defaults to null, and every document would take the hash-only path until its
    // next real change, which is slow rather than wrong. It is refused anyway, because the rule here is that
    // a manifest is read only by the writer of its shape: a reader that starts reasoning about which slots a
    // file predates is one bump away from reasoning wrongly.
    [InlineData(26)]
    public void ReadAndValidate_SchemaVersionOtherThanTheCurrentOne_ReturnsMiss(int schemaVersion)
    {
        // Arrange
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution))
            .ShouldBeTrue();

        // Act — restamp the cache with a version this reader does not write.
        solution.MutateCacheJson(root => root["SchemaVersion"] = schemaVersion);

        // Assert — an off-schema cache degrades cleanly to a rebuild, never a wrong answer.
        store.ReadAndValidate()
            .Outcome.ShouldBe(CacheOutcome.Miss);
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
            solution.Projects.Select(p => new CodebaseFragment(p.ProjectName, null, p.ProjectReferences, [], [], [], [], [], [], [], [], [], []))
                .ToList(),
            [new SpecResolutionRecord("", "A", ["A"], ["/out/A.dll"], null)],
            WorkspaceDiagnostics.None);
        store.Write(store.CaptureFingerprint(solution.Projects), extraction)
            .ShouldBeTrue();

        solution.MutateCacheJson(root =>
        {
            root["SchemaVersion"] = 9;
            var record = (JsonObject)root["SpecResolutions"]![0]!;
            record.Remove("ExcludeProjectNames");
            record["ExcludeProjectName"] = "A";
        });

        store.ReadAndValidate()
            .Outcome.ShouldBe(CacheOutcome.Miss);
    }

    [Fact]
    public void ReadAndValidate_ArtifactsLayoutAssetsFileChanged_ReturnsMiss()
    {
        // The defect this pins was measured in the field, not read off the code: under
        // UseArtifactsOutput the assets file lands outside the project directory, the probe watched
        // <projectDirectory>/obj/project.assets.json — a path no build ever creates — and a re-restore that
        // changed the model was served from cache as though nothing had moved.
        using var solution = new SyntheticSolution();
        solution.AddArtifactsLayoutProject("A", ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution))
            .ShouldBeTrue();

        // The control: with nothing touched, this layout hits like any other.
        store.ReadAndValidate()
            .Outcome.ShouldBe(CacheOutcome.Hit);

        // A restore rewrote the assets file, and nothing else on disk moved — no source edit, no csproj
        // edit, no probe-chain file. The assets file is the only input carrying the change.
        File.WriteAllText(solution.ArtifactsAssetsPathOf("A"), "{\"libraries\":{}}\n");

        store.ReadAndValidate()
            .Outcome.ShouldBe(CacheOutcome.Miss);
    }

    [Fact]
    public void CaptureFingerprint_ArtifactsLayout_StampsTheAssetsFileWhereTheLayoutPutIt()
    {
        // The mechanism behind the fact above, so a regression names its cause rather than only its effect:
        // the default location is still stamped (absent — that is what makes an appearing file a flip), and
        // the location the layout actually used is stamped too, present.
        using var solution = new SyntheticSolution();
        solution.AddArtifactsLayoutProject("A", ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution))
            .ShouldBeTrue();

        IReadOnlyDictionary<string, bool> stamps = solution.StructuralStampsByPath();

        stamps.ShouldContainKey(solution.ArtifactsAssetsPathOf("A"));
        stamps[solution.ArtifactsAssetsPathOf("A")]
            .ShouldBeTrue();
        stamps.ShouldContainKey(solution.DefaultLayoutAssetsPathOf("A"));
        stamps[solution.DefaultLayoutAssetsPathOf("A")]
            .ShouldBeFalse();
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
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution))
            .ShouldBeTrue();

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
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution))
            .ShouldBeTrue();

        // Act — a cache from a different tool build (commit) is discarded.
        solution.MutateCacheJson(root => root["ToolVersion"] = "0.0.0-not-this-build");

        // Assert
        store.ReadAndValidate()
            .Outcome.ShouldBe(CacheOutcome.Miss);
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
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution))
            .ShouldBeTrue();

        byte[] bytes = File.ReadAllBytes(solution.CacheFilePath);
        File.WriteAllBytes(solution.CacheFilePath, bytes[..(bytes.Length / 2)]);

        // Act + Assert
        Should.NotThrow(() => store.ReadAndValidate())
            .Outcome.ShouldBe(CacheOutcome.Miss);
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
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution))
            .ShouldBeTrue();
        store.ReadAndValidate()
            .Outcome.ShouldBe(CacheOutcome.Hit); // baseline: clean before the file appears

        // Act — the recorded-absent probe file appears. The store stats and hashes rather than parsing, so
        // the bytes carry no meaning here; the file existing is the whole signal.
        File.WriteAllText(Path.Combine(solution.Root, probeFileName), "probe\n");

        // Assert — an existence flip on a structural probe is a full miss.
        store.ReadAndValidate()
            .Outcome.ShouldBe(CacheOutcome.Miss);
    }

    [Fact]
    public void ReadAndValidate_MtimeBumpedSameContent_RehashesOnceThenStatFastPath()
    {
        // Arrange — backdate so every stamp is captured promoted, then write.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"), ("B.cs", "class B {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution))
            .ShouldBeTrue();

        // A clean tree is a pure-stat hit: zero content reads even on the first validation.
        long baseline = store.ContentHashCount;
        store.ReadAndValidate()
            .Outcome.ShouldBe(CacheOutcome.Hit);
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
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution))
            .ShouldBeTrue();

        // Act — change A's content; the Merkle keys carry the change to every dependent.
        File.WriteAllText(solution.PathOf("A", "A.cs"), "class A { int x; }");
        File.SetLastWriteTimeUtc(solution.PathOf("A", "A.cs"), DateTime.UtcNow.AddHours(-1));
        CacheReadResult result = store.ReadAndValidate();

        // Assert — {A, B, C} dirty (D clean); the reusable fragments are exactly the clean projects'.
        result.Outcome.ShouldBe(CacheOutcome.Partial);
        result.DirtyProjects.ShouldBe(["A", "B", "C"], true);
        result.ReusableFragments.Select(f => f.ProjectName)
            .ShouldBe(["D"]);
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
        File.Exists(solution.CacheFilePath)
            .ShouldBeFalse();
    }

    [Fact]
    public void Write_ValidCacheOverwrittenByNewWrite_ReadsBackTheNewContent()
    {
        // Arrange
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();

        store.Write(store.CaptureFingerprint(solution.Projects), OneFragment(solution, "first"))
            .ShouldBeTrue();
        store.ReadAndValidate()
            .LoadDiagnostics.LoadFailures.ShouldBe(["first"]);

        // Act — a second atomic write fully replaces the file.
        store.Write(store.CaptureFingerprint(solution.Projects), OneFragment(solution, "second"))
            .ShouldBeTrue();
        CacheReadResult result = store.ReadAndValidate();

        // Assert — the new content, whole (no partial state from the overwrite).
        result.Outcome.ShouldBe(CacheOutcome.Hit);
        result.LoadDiagnostics.LoadFailures.ShouldBe(["second"]);
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
        SpecResolutionRecord[] specs = [new("", "A", ["A", "PrivatePack"], ["/out/A.dll"], "/obj/A.dll")];
        var extraction = new ExtractionResult(
            solution.Projects.Select(p => new CodebaseFragment(p.ProjectName, null, p.ProjectReferences, [], [], [], [], [], [], [], [], [], []))
                .ToList(),
            specs,
            new WorkspaceDiagnostics(
                ["load-diag-1", "load-diag-2"], [], ["/repo/Broken/Broken.csproj"], [],
                ["/repo/Unrestored/Unrestored.csproj"],
                [
                    new UnsupportedProject("/repo/Fs/Fs.fsproj", UnsupportedProjectKind.NotCsharp),
                    new UnsupportedProject("/repo/Shared/Shared.shproj", UnsupportedProjectKind.SharedProject)
                ],
                []));
        store.Write(store.CaptureFingerprint(solution.Projects), extraction)
            .ShouldBeTrue();

        // Act
        CacheReadResult result = store.ReadAndValidate();

        // Assert — a full hit returns every fragment plus the recorded spec resolutions and diagnostics.
        result.Outcome.ShouldBe(CacheOutcome.Hit);
        result.ReusableFragments.Select(f => f.ProjectName)
            .ShouldBe(["A", "B"], true);
        result.DirtyProjects.ShouldBeEmpty();
        // Field-by-field: the record carries a collection, so its synthesized equality compares that member
        // by reference and a round-tripped list can never equal the written one.
        SpecResolutionRecord replayed = result.SpecResolutions.ShouldHaveSingleItem();
        replayed.NormalizedSpecArgument.ShouldBe("");
        replayed.SpecProjectName.ShouldBe("A");
        replayed.ExcludeProjectNames.ShouldBe(["A", "PrivatePack"]);
        replayed.OutputFilePaths.ShouldBe(["/out/A.dll"]);
        // The intermediate assembly path round-trips too: a hit that lost it would resolve a built output the
        // cold run refused, which is the whole reason it is persisted rather than recomputed.
        replayed.IntermediateAssemblyPath.ShouldBe("/obj/A.dll");
        result.LoadDiagnostics.LoadFailures.ShouldBe(["load-diag-1", "load-diag-2"]);
        // And both halves of the gate's own input round-trip beside them. A hit owns no workspace to recompute
        // either from, so a manifest that lost one would answer green exactly where the cold run refuses.
        result.LoadDiagnostics.FailedProjects.ShouldBe(["/repo/Broken/Broken.csproj"]);
        result.LoadDiagnostics.RestoreFailedProjects.ShouldBe(["/repo/Unrestored/Unrestored.csproj"]);
        // The coverage statement round-trips with its classification, not just its paths. The kind is the
        // producer's verdict and a hit owns no solution file to re-read it from, so a manifest that stored
        // paths alone would leave the reason to be guessed again on the one path that cannot check.
        result.LoadDiagnostics.UnsupportedProjects.ShouldBe([
            new UnsupportedProject("/repo/Fs/Fs.fsproj", UnsupportedProjectKind.NotCsharp),
            new UnsupportedProject("/repo/Shared/Shared.shproj", UnsupportedProjectKind.SharedProject)
        ]);
    }

    [Fact]
    public void ReadAndValidate_MultiTargetedProject_HasOneProjectEntryAndTwoFragments()
    {
        // Arrange — the shape a multi-target-framework csproj produces: its several Projects collapse to ONE
        // fingerprinted project entry (one csproj, one document set, one pair of invalidation keys) while
        // extraction yields one fragment per framework. The two sides key on the same project name, which is
        // the whole reason they can meet.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", "class A {}"));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        var extraction = new ExtractionResult(
            [
                new CodebaseFragment("A", "net10.0", [], [], [], [], [], [], [], [], [], [], []),
                new CodebaseFragment("A", "netstandard2.0", [], [], [], [], [], [], [], [], [], [], [])
            ],
            [],
            WorkspaceDiagnostics.None);
        store.Write(store.CaptureFingerprint(solution.Projects), extraction)
            .ShouldBeTrue();

        // Act
        CacheReadResult result = store.ReadAndValidate();

        // Assert — a clean hit replays both fragments under the one name, each still carrying its framework.
        result.Outcome.ShouldBe(CacheOutcome.Hit);
        result.ReusableFragments.Select(fragment => fragment.ProjectName)
            .ShouldBe(["A", "A"]);
        result.ReusableFragments.Select(fragment => fragment.TargetFramework)
            .ShouldBe(["net10.0", "netstandard2.0"]);
        solution.CacheProjectEntryCount()
            .ShouldBe(1);
    }

    // ── trivia-only edits: remap rather than dirty ──────────────────────────────────────────────────────

    [Fact]
    public void ReadAndValidate_CommentOnlyEdit_HitsWithSitesMovedAndRehashesOnce()
    {
        // Arrange — one project whose fragment carries sites on the two field lines of its only document.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", TwoFieldClass("A")));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), SitedExtraction(solution, ("A", "A.cs", [3, 4])))
            .ShouldBeTrue();

        // The control: a clean tree hits without opening a file.
        long beforeClean = store.ContentHashCount;
        store.ReadAndValidate()
            .Outcome.ShouldBe(CacheOutcome.Hit);
        (store.ContentHashCount - beforeClean).ShouldBe(0);

        // Act — two comment lines above the class. Every fact the fragment holds is still true; only the
        // lines it holds them at have moved.
        PrependLines(solution.PathOf("A", "A.cs"), "// a note", "// and another");
        long beforeEdit = store.ContentHashCount;
        CacheReadResult afterEdit = store.ReadAndValidate();

        // Assert — a hit, one read (the edited document), and the sites moved down by the two comment lines.
        afterEdit.Outcome.ShouldBe(CacheOutcome.Hit);
        (store.ContentHashCount - beforeEdit).ShouldBe(1);
        store.LastRemappedDocuments.ShouldBe([Path.GetFullPath(solution.PathOf("A", "A.cs"))]);
        SiteLinesOf(afterEdit, "A")
            .ShouldBe([5, 6]);

        // Act 2 — the promoted manifest describes the edited tree, so the next validation is pure-stat.
        byte[] afterPromotion = File.ReadAllBytes(solution.CacheFilePath);
        long beforeSettled = store.ContentHashCount;
        CacheReadResult settled = store.ReadAndValidate();

        // Assert 2 — nothing read, nothing remapped, and nothing rewritten: no perpetual churn.
        settled.Outcome.ShouldBe(CacheOutcome.Hit);
        (store.ContentHashCount - beforeSettled).ShouldBe(0);
        store.LastRemappedDocuments.ShouldBeEmpty();
        File.ReadAllBytes(solution.CacheFilePath)
            .ShouldBe(afterPromotion);
    }

    [Fact]
    public void ReadAndValidate_CommentOnlyEditInADependency_LeavesTheDependentClean()
    {
        // Arrange — B references A, and both fragments carry sites in their own document.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", TwoFieldClass("A")));
        solution.AddProject("B", ["A"], ("B.cs", TwoFieldClass("B")));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        ExtractionResult extraction = SitedExtraction(solution, ("A", "A.cs", [3, 4]), ("B", "B.cs", [3, 4]));
        store.Write(store.CaptureFingerprint(solution.Projects), extraction)
            .ShouldBeTrue();

        // Act — a comment edit in the dependency alone.
        PrependLines(solution.PathOf("A", "A.cs"), "// a note", "// and another");
        CacheReadResult result = store.ReadAndValidate();

        // Assert — the dependent is not dirtied by a change that moved no fact, and its own sites are
        // untouched: the map belongs to A.cs, and B holds nothing there.
        result.Outcome.ShouldBe(CacheOutcome.Hit);
        result.DirtyProjects.ShouldBeEmpty();
        SiteLinesOf(result, "A")
            .ShouldBe([5, 6]);
        SiteLinesOf(result, "B")
            .ShouldBe([3, 4]);
        store.LastRemappedDocuments.ShouldBe([Path.GetFullPath(solution.PathOf("A", "A.cs"))]);
    }

    [Fact]
    public void ReadAndValidate_StringLiteralEdit_PartialWithDependents()
    {
        // Arrange — A's document carries a string literal, which is a token and therefore part of its shape.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", ClassWithLiteral("A", "before")));
        solution.AddProject("B", ["A"], ("B.cs", TwoFieldClass("B")));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), SitedExtraction(solution, ("A", "A.cs", [3, 4])))
            .ShouldBeTrue();

        // Act — the literal's text changes and nothing else does.
        RewriteBackdated(solution.PathOf("A", "A.cs"), ClassWithLiteral("A", "after"));
        CacheReadResult result = store.ReadAndValidate();

        // Assert — an edit inside a token is an ordinary content change, so the dependent goes with it.
        result.Outcome.ShouldBe(CacheOutcome.Partial);
        result.DirtyProjects.ShouldBe(["A", "B"], true);
    }

    [Fact]
    public void ReadAndValidate_PragmaInserted_PartialWithDependents()
    {
        // Arrange
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", TwoFieldClass("A")));
        solution.AddProject("B", ["A"], ("B.cs", TwoFieldClass("B")));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), SitedExtraction(solution, ("A", "A.cs", [3, 4])))
            .ShouldBeTrue();

        // Act — a directive, not a comment. It changes what the compiler sees, so it changes the shape.
        PrependLines(solution.PathOf("A", "A.cs"), "#pragma warning disable CS0169");
        CacheReadResult result = store.ReadAndValidate();

        // Assert
        result.Outcome.ShouldBe(CacheOutcome.Partial);
        result.DirtyProjects.ShouldBe(["A", "B"], true);
    }

    [Fact]
    public void ReadAndValidate_SiteOnATokenlessLine_DirtiesThatProjectOnly()
    {
        // Arrange — A's fragment claims a site on the blank line between its two fields. A blank line starts
        // no token, so the map that a comment edit yields has no entry for it.
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", TwoFieldClassWithGap("A")));
        solution.AddProject("B", ["A"], ("B.cs", TwoFieldClass("B")));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        ExtractionResult extraction = SitedExtraction(solution, ("A", "A.cs", [4]), ("B", "B.cs", [3, 4]));
        store.Write(store.CaptureFingerprint(solution.Projects), extraction)
            .ShouldBeTrue();

        // Act
        PrependLines(solution.PathOf("A", "A.cs"), "// a note", "// and another");
        CacheReadResult result = store.ReadAndValidate();

        // Assert — A alone re-extracts. B is left clean because the equivalence it relies on — what A's
        // compilation exposes — is exactly what a trivia-only edit cannot have moved.
        result.Outcome.ShouldBe(CacheOutcome.Partial);
        result.DirtyProjects.ShouldBe(["A"]);
        result.ReusableFragments.Select(fragment => fragment.ProjectName)
            .ShouldBe(["B"]);
    }

    [Fact]
    public void ReadAndValidate_TokenBearingLineSplit_PartialWithDependents()
    {
        // Arrange
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", TwoFieldClass("A")));
        solution.AddProject("B", ["A"], ("B.cs", TwoFieldClass("B")));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();
        store.Write(store.CaptureFingerprint(solution.Projects), SitedExtraction(solution, ("A", "A.cs", [3, 4])))
            .ShouldBeTrue();

        // Act — the same tokens, on more lines: `int x;` becomes `int` and `x;`. The shape is unchanged, but
        // one old line's tokens now sit on two, so no line map can say where that line went.
        RewriteBackdated(solution.PathOf("A", "A.cs"), "class A\n{\n    int\n        x;\n    int y;\n}\n");
        CacheReadResult result = store.ReadAndValidate();

        // Assert
        result.Outcome.ShouldBe(CacheOutcome.Partial);
        result.DirtyProjects.ShouldBe(["A", "B"], true);
    }

    [Fact]
    public void CaptureFingerprint_StampsEachDocumentWithItsShape()
    {
        // Arrange
        using var solution = new SyntheticSolution();
        solution.AddProject("A", [], ("A.cs", TwoFieldClass("A")), ("B.cs", TwoFieldClassWithGap("B")));
        solution.BackdateAll();
        ExtractionCacheStore store = solution.NewStore();

        // Act
        store.Write(store.CaptureFingerprint(solution.Projects), TrivialExtraction(solution))
            .ShouldBeTrue();

        // Assert — the shape is the half of a document stamp that tells a trivia-only edit from a real one,
        // so every document carries one: a digest in the same lowercase-hex form as the content hash beside
        // it, and one token count per line of the file.
        IReadOnlyList<JsonObject> documents = solution.CacheDocumentStamps();
        documents.Count.ShouldBe(2);
        foreach (JsonObject document in documents)
        {
            var path = document["Path"]!.GetValue<string>();
            JsonNode shape = document["Shape"].ShouldNotBeNull();
            shape["Hash"]!.GetValue<string>()
                .ShouldMatch("^[0-9a-f]{64}$");
            shape["TokensPerLine"]!.AsArray()
                .Count.ShouldBe(File.ReadAllText(path)
                    .Split('\n')
                    .Length);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────

    // Real C# whose every named line starts with a token, so a site on one is a site a line map can move.
    private static string TwoFieldClass(string name)
    {
        return $"class {name}\n{{\n    int x;\n    int y;\n}}\n";
    }

    // The same, with a blank line 4 — a line no token starts, and so one no map has an entry for.
    private static string TwoFieldClassWithGap(string name)
    {
        return $"class {name}\n{{\n    int x;\n\n    int y;\n}}\n";
    }

    private static string ClassWithLiteral(string name, string literal)
    {
        return $"class {name}\n{{\n    string s = \"{literal}\";\n    int y;\n}}\n";
    }

    // The trivial fragments carry no sites at all, so nothing in them can be seen to move. Each placement
    // gives one project's fragment a single edge whose sites sit on the named lines of the named file — an
    // edge is three strings and a site list, so no compilation is needed to mint a real one.
    private static ExtractionResult SitedExtraction(
        SyntheticSolution solution, params (string Project, string File, int[] Lines)[] placements)
    {
        List<CodebaseFragment> fragments = solution.Projects
            .Select(project => new CodebaseFragment(
                project.ProjectName, null, project.ProjectReferences, [], [],
                EdgesFor(solution, project.ProjectName, placements), [], [], [], [], [], [], []))
            .ToList();
        return new ExtractionResult(fragments, [], new WorkspaceDiagnostics(["diag"], [], [], [], [], [], []));
    }

    private static IReadOnlyList<FragmentEdge> EdgesFor(
        SyntheticSolution solution, string projectName, IReadOnlyList<(string Project, string File, int[] Lines)> placements)
    {
        return placements
            .Where(placement => string.Equals(placement.Project, projectName, StringComparison.Ordinal))
            .Select(placement => new FragmentEdge(
                $"N.{placement.Project}.Source",
                "N.Target",
                placement.Lines.Select(line => new FragmentSite(Path.GetFullPath(solution.PathOf(placement.Project, placement.File)), line))
                    .ToList()))
            .ToList();
    }

    private static IReadOnlyList<int> SiteLinesOf(CacheReadResult result, string projectName)
    {
        return result.ReusableFragments
            .Single(fragment => string.Equals(fragment.ProjectName, projectName, StringComparison.Ordinal))
            .Edges.Single()
            .Sites.Select(site => site.Line)
            .ToList();
    }

    // Rewrites a document and puts its mtime an hour into the past, so the next capture stamps it promoted
    // and the sweep that follows takes the stat fast path rather than re-reading it for the racy window.
    private static void RewriteBackdated(string path, string content)
    {
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));
    }

    private static void PrependLines(string path, params string[] lines)
    {
        string prefix = string.Concat(lines.Select(line => line + "\n"));
        RewriteBackdated(path, prefix + File.ReadAllText(path));
    }

    private static ExtractionResult TrivialExtraction(SyntheticSolution solution)
    {
        List<CodebaseFragment> fragments = solution.Projects
            .Select(p => new CodebaseFragment(p.ProjectName, null, p.ProjectReferences, [], [], [], [], [], [], [], [], [], []))
            .ToList();
        return new ExtractionResult(fragments, [], new WorkspaceDiagnostics(["diag"], [], [], [], [], [], []));
    }

    private static ExtractionResult OneFragment(SyntheticSolution solution, string diagnostic)
    {
        List<CodebaseFragment> fragments = solution.Projects
            .Select(p => new CodebaseFragment(p.ProjectName, null, p.ProjectReferences, [], [], [], [], [], [], [], [], [], []))
            .ToList();
        return new ExtractionResult(fragments, [], new WorkspaceDiagnostics([diagnostic], [], [], [], [], [], []));
    }

    /// <summary>
    ///     A throwaway synthetic solution tree under <c>%TEMP%</c>: a fake <c>.sln</c>, and per project a
    ///     directory with a fake <c>.csproj</c>, an <c>obj/project.assets.json</c>, and source files. The
    ///     cache root is a sibling directory, so the store's own writes never collide with the fake inputs.
    /// </summary>
    private sealed class SyntheticSolution : IDisposable
    {
        private readonly List<ProjectInputs> projects = [];

        private readonly TempDirectory temp = TestTempRoot.Fresh("cache-store");

        public SyntheticSolution()
        {
            Root = temp.Path;
            CacheRoot = Path.Combine(Root, "cache");
            SolutionPath = Path.Combine(Root, "App.sln");
            File.WriteAllText(SolutionPath, "Microsoft Visual Studio Solution File\n");
        }

        public string Root { get; }

        private string CacheRoot { get; }

        private string SolutionPath { get; }

        public string CacheFilePath => CacheLocations.CacheFilePath(SolutionPath, CacheRoot);

        public IReadOnlyList<ProjectInputs> Projects => projects;

        public void Dispose()
        {
            temp.Dispose();
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

        // A project built under UseArtifactsOutput: its intermediate tree — project.assets.json included —
        // lives under a solution-level artifacts/ directory, not beside the project. The two evaluated paths
        // are what a loaded workspace hands the store, and they are the only way the assets file's real
        // location can be derived.
        public void AddArtifactsLayoutProject(string name, params (string File, string Content)[] documents)
        {
            string directory = Path.Combine(Root, "src", name);
            Directory.CreateDirectory(directory);
            string csproj = Path.Combine(directory, $"{name}.csproj");
            File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");

            string intermediateDirectory = Path.Combine(Root, "artifacts", "obj", name, "debug");
            Directory.CreateDirectory(intermediateDirectory);
            File.WriteAllText(ArtifactsAssetsPathOf(name), "{}\n");

            var documentPaths = new List<string>();
            foreach ((string file, string content) in documents)
            {
                string documentPath = Path.Combine(directory, file);
                File.WriteAllText(documentPath, content);
                documentPaths.Add(documentPath);
            }

            projects.Add(new ProjectInputs(
                name,
                csproj,
                directory,
                [],
                documentPaths,
                Path.Combine(Root, "artifacts", "bin", name, "debug", $"{name}.dll"),
                Path.Combine(intermediateDirectory, $"{name}.dll")));
        }

        public string ArtifactsAssetsPathOf(string name)
        {
            return Path.GetFullPath(Path.Combine(Root, "artifacts", "obj", name, "project.assets.json"));
        }

        public string DefaultLayoutAssetsPathOf(string name)
        {
            return Path.GetFullPath(Path.Combine(Root, "src", name, "obj", "project.assets.json"));
        }

        // Every document stamp the written manifest carries, across all its projects, as the raw JSON the
        // store wrote — the level a claim about a persisted slot has to be read at.
        public IReadOnlyList<JsonObject> CacheDocumentStamps()
        {
            var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CacheFilePath))!;
            return root["Projects"]!.AsArray()
                .SelectMany(project => project!["Documents"]!.AsArray())
                .Select(document => (JsonObject)document!)
                .ToList();
        }

        // Every structural stamp the written manifest carries, path -> whether the file existed at capture.
        public IReadOnlyDictionary<string, bool> StructuralStampsByPath()
        {
            var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CacheFilePath))!;
            return root["StructuralStamps"]!.AsArray()
                .ToDictionary(stamp => stamp!["Path"]!.GetValue<string>(), stamp => stamp!["Exists"]!.GetValue<bool>());
        }

        // Writes a *.cs into an existing project's directory WITHOUT recording it as a document — the
        // on-disk-but-excluded stray (a <Compile Remove> file) the cone scan sees but the compiler does not.
        public void AddStrayFile(string project, string relativePath, string content)
        {
            temp.WriteFile([project, relativePath], content);
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
        // for the stat fast path. The cache root lives inside the tree, so it is excluded: the store's own
        // writes must keep the times it wrote them with.
        public void BackdateAll()
        {
            FixtureEdits.BackdateTree(Root, CacheRoot);
        }

        // The number of project entries the written manifest carries — the fingerprint's own grain, which a
        // multi-framework project must not multiply however many fragments it extracted to.
        public int CacheProjectEntryCount()
        {
            var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CacheFilePath))!;
            return root["Projects"]!.AsArray()
                .Count;
        }

        public void MutateCacheJson(Action<JsonObject> mutate)
        {
            var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CacheFilePath))!;
            mutate(root);
            File.WriteAllText(CacheFilePath, root.ToJsonString());
        }
    }
}
