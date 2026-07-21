using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Replay;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Replay;

/// <summary>
///     Tests for <see cref="BinlogCaptureStore" /> over the shared MyApp binlog fixture. Every test points
///     the store at its own throwaway cache root, so the persisted <c>capture.json</c>/<c>capture.binlog</c>
///     are per-test; the only shared mutable state is the fixture tree, so any test that mutates it reverts
///     byte- and mtime-safely in a <c>finally</c> (a structural file left newer than the binlog would trip a
///     later test's ingest staleness check). Shares the one serial world with the fidelity tests — both
///     mutate the same assembly-wide copy, so they must never run at once.
/// </summary>
/// <remarks>
///     The headline pin is the structure-only contract: a content edit to a tracked source file leaves the
///     capture <see cref="CaptureState.Usable" />, because replay reads text from current disk. Everything
///     else invalidates: structural edits, source adds/removes, a garbled or version-mismatched manifest.
/// </remarks>
[Collection("Serial")]
public sealed class BinlogCaptureStoreTests : IDisposable
{
    private readonly string _cacheRoot =
        Path.Combine(Path.GetTempPath(), "loadbearing-capture-tests", Guid.NewGuid().ToString("N"));

    private static BinlogFixtureWorkspace Fixture => BinlogFixtureWorkspace.Instance;

    public void Dispose()
    {
        TryDeleteDirectory(_cacheRoot);
    }

    // ── happy path + the structure-only contract ─────────────────────────────────────────────────────────

    [Fact]
    public void Ingest_HappyPath_PersistsPairAndValidatesUsable()
    {
        // Arrange + Act
        using ReplayedSolution replayed = BinlogReplayer.Replay(Fixture.BinlogPath);
        var store = new BinlogCaptureStore(Fixture.SolutionPath, _cacheRoot);
        bool persisted = store.Ingest(replayed.Solution, Fixture.BinlogPath, Fixture.BinlogPath);

        // Assert — both files land under the override root, and validation points at the copy.
        persisted.ShouldBeTrue();
        File.Exists(CacheLocations.CaptureManifestPath(Fixture.SolutionPath, _cacheRoot)).ShouldBeTrue();
        File.Exists(CacheLocations.CaptureBinlogPath(Fixture.SolutionPath, _cacheRoot)).ShouldBeTrue();

        CaptureValidation validation = store.Validate();
        validation.State.ShouldBe(CaptureState.Usable);
        validation.BinlogCopyPath.ShouldBe(CacheLocations.CaptureBinlogPath(Fixture.SolutionPath, _cacheRoot));
    }

    [Fact]
    public void Validate_ContentEditToTrackedSource_StaysUsable()
    {
        // Arrange — the headline pin: replay reads text from current disk, so a content edit never
        // invalidates a structure-only capture.
        BinlogCaptureStore store = IngestFullCapture();
        string source = Fixture.PathOf("MyApp.Domain", "Money.cs");
        byte[] original = File.ReadAllBytes(source);
        DateTime originalMtime = File.GetLastWriteTimeUtc(source);
        try
        {
            File.AppendAllText(source, $"{Environment.NewLine}public sealed class CaptureContentEditProbe {{ }}{Environment.NewLine}");

            // Act
            CaptureValidation validation = store.Validate();

            // Assert
            validation.State.ShouldBe(CaptureState.Usable);
        }
        finally
        {
            File.WriteAllBytes(source, original);
            File.SetLastWriteTimeUtc(source, originalMtime);
        }
    }

    [Fact]
    public void Validate_BareCsprojTouch_RehashesOnceThenStatFastPath()
    {
        // Arrange
        BinlogCaptureStore store = IngestFullCapture();
        string csproj = Fixture.PathOf("MyApp.Domain", "MyApp.Domain.csproj");
        var original = Snapshot(csproj);
        try
        {
            // A warm-up validation promotes any structural stamp that was still racy at ingest, so the only
            // re-hash the post-touch validation can incur is the touched csproj itself.
            store.Validate().State.ShouldBe(CaptureState.Usable);

            // Act 1 — rewrite identical bytes and set a different, comfortably-past mtime (a bare touch).
            File.WriteAllBytes(csproj, original.bytes);
            File.SetLastWriteTimeUtc(csproj, DateTime.UtcNow.AddHours(-1));
            long beforeTouch = store.ContentHashCount;
            CaptureValidation afterTouch = store.Validate();

            // Assert 1 — exactly one re-hash (the touched csproj), still usable (bytes unchanged).
            afterTouch.State.ShouldBe(CaptureState.Usable);
            (store.ContentHashCount - beforeTouch).ShouldBe(1);

            // Act 2 — validate again with nothing touched.
            long beforeSecond = store.ContentHashCount;
            CaptureValidation second = store.Validate();

            // Assert 2 — the promoted stamp means the steady state hashes nothing.
            second.State.ShouldBe(CaptureState.Usable);
            (store.ContentHashCount - beforeSecond).ShouldBe(0);
        }
        finally
        {
            Restore(csproj, original);
        }
    }

    // ── structural / membership invalidation ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_CsprojContentEdit_InvalidNamingCsproj()
    {
        // Arrange
        using ReplayedSolution replayed = BinlogReplayer.Replay(Fixture.BinlogPath);
        var store = new BinlogCaptureStore(Fixture.SolutionPath, _cacheRoot);
        store.Ingest(replayed.Solution, Fixture.BinlogPath, Fixture.BinlogPath).ShouldBeTrue();

        string csproj = CsprojPathOf(replayed.Solution, "MyApp.Domain");
        var original = Snapshot(csproj);
        try
        {
            string edited = File.ReadAllText(csproj).Replace("</Project>", "  <!-- capture stale probe -->\n</Project>");
            File.WriteAllText(csproj, edited);

            // Act
            CaptureValidation validation = store.Validate();

            // Assert — a structural content change is the stale variant naming that csproj.
            validation.State.ShouldBe(CaptureState.Invalid);
            validation.Notice.ShouldBe(BinlogCaptureStore.StaleNotice(csproj));
        }
        finally
        {
            Restore(csproj, original);
        }
    }

    [Fact]
    public void Validate_NewSourceFileInCone_InvalidNamingFile()
    {
        // Arrange
        BinlogCaptureStore store = IngestFullCapture();
        string added = Fixture.PathOf("MyApp.Domain", "CaptureConeAddProbe.cs");
        try
        {
            File.WriteAllText(added, "namespace MyApp.Domain; public sealed class CaptureConeAddProbe { }\n");

            // Act
            CaptureValidation validation = store.Validate();

            // Assert — an unrecorded *.cs in a project cone is a membership change, naming the file.
            validation.State.ShouldBe(CaptureState.Invalid);
            validation.Notice.ShouldBe(BinlogCaptureStore.StaleNotice(Path.GetFullPath(added)));
        }
        finally
        {
            File.Delete(added);
        }
    }

    [Fact]
    public void Validate_NewFileUnderObj_StaysUsable()
    {
        // Arrange
        BinlogCaptureStore store = IngestFullCapture();
        string objProbe = Fixture.PathOf("MyApp.Domain", "obj", "CaptureObjProbe.cs");
        try
        {
            File.WriteAllText(objProbe, "namespace MyApp.Domain; public sealed class CaptureObjProbe { }\n");

            // Act + Assert — the cone scan skips bin/obj, so a generated-output add never invalidates.
            store.Validate().State.ShouldBe(CaptureState.Usable);
        }
        finally
        {
            File.Delete(objProbe);
        }
    }

    [Fact]
    public void Validate_ExcludedStrayInCone_StaysUsableWithNoNotice()
    {
        // Arrange — MyApp.Domain carries a <Compile Remove>'d Snippets/*.cs: it lives in the project cone on
        // disk but is not a compiled document, so it is absent from the capture's DocumentPaths. Before the
        // fix the cone scan read it as an add and invalidated the capture on every run; the ConeFiles
        // snapshot recorded at ingest now covers it. This static-fixture stray needs no revert.
        BinlogCaptureStore store = IngestFullCapture();
        File.Exists(Fixture.PathOf("MyApp.Domain", "Snippets", "ExcludedScratch.cs")).ShouldBeTrue();

        // Act
        CaptureValidation validation = store.Validate();

        // Assert — Usable, and specifically with no stale/unreadable notice.
        validation.State.ShouldBe(CaptureState.Usable);
        validation.Notice.ShouldBeNull();
    }

    [Fact]
    public void Validate_DeletedRecordedDocument_InvalidNamingFile()
    {
        // Arrange
        using ReplayedSolution replayed = BinlogReplayer.Replay(Fixture.BinlogPath);
        var store = new BinlogCaptureStore(Fixture.SolutionPath, _cacheRoot);
        store.Ingest(replayed.Solution, Fixture.BinlogPath, Fixture.BinlogPath).ShouldBeTrue();

        string document = DocumentPathOf(replayed.Solution, "MyApp.Legacy.Billing", "RoundingMode.cs");
        byte[] original = File.ReadAllBytes(document);
        DateTime originalMtime = File.GetLastWriteTimeUtc(document);
        try
        {
            File.Delete(document);

            // Act
            CaptureValidation validation = store.Validate();

            // Assert — a recorded document that has vanished (a delete or a clean) is stale, naming it.
            validation.State.ShouldBe(CaptureState.Invalid);
            validation.Notice.ShouldBe(BinlogCaptureStore.StaleNotice(document));
        }
        finally
        {
            File.WriteAllBytes(document, original);
            File.SetLastWriteTimeUtc(document, originalMtime);
        }
    }

    // ── unreadable / version / absent ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_GarbledManifest_InvalidUnreadable()
    {
        // Arrange
        BinlogCaptureStore store = IngestFullCapture();
        File.WriteAllText(CacheLocations.CaptureManifestPath(Fixture.SolutionPath, _cacheRoot), "{ not: valid json ]");

        // Act
        CaptureValidation validation = store.Validate();

        // Assert
        validation.State.ShouldBe(CaptureState.Invalid);
        validation.Notice.ShouldBe(BinlogCaptureStore.UnreadableNotice);
    }

    [Fact]
    public void Validate_DeletedBinlogCopy_InvalidUnreadable()
    {
        // Arrange
        BinlogCaptureStore store = IngestFullCapture();
        File.Delete(CacheLocations.CaptureBinlogPath(Fixture.SolutionPath, _cacheRoot));

        // Act
        CaptureValidation validation = store.Validate();

        // Assert — a manifest with no binlog to replay is unreadable, not stale.
        validation.State.ShouldBe(CaptureState.Invalid);
        validation.Notice.ShouldBe(BinlogCaptureStore.UnreadableNotice);
    }

    [Fact]
    public void Validate_ToolVersionMismatch_InvalidVersion()
    {
        // Arrange
        BinlogCaptureStore store = IngestFullCapture();
        MutateManifest(root => root["ToolVersion"] = "0.0.0-not-this-build");

        // Act
        CaptureValidation validation = store.Validate();

        // Assert
        validation.State.ShouldBe(CaptureState.Invalid);
        validation.Notice.ShouldBe(BinlogCaptureStore.VersionMismatchNotice);
    }

    [Fact]
    public void Validate_SchemaVersionMismatch_InvalidUnreadable()
    {
        // Arrange
        BinlogCaptureStore store = IngestFullCapture();
        MutateManifest(root => root["SchemaVersion"] = 999);

        // Act
        CaptureValidation validation = store.Validate();

        // Assert — a schema the reader does not understand is unreadable.
        validation.State.ShouldBe(CaptureState.Invalid);
        validation.Notice.ShouldBe(BinlogCaptureStore.UnreadableNotice);
    }

    [Fact]
    public void Validate_NoCapture_Absent()
    {
        // Arrange — a store over a solution with nothing ingested.
        var store = new BinlogCaptureStore(Fixture.SolutionPath, _cacheRoot);

        // Act + Assert — silent absence; the cold path runs with no notice.
        store.Validate().State.ShouldBe(CaptureState.Absent);
    }

    // ── ingest refusals ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Ingest_StructuralFileNewerThanBinlog_ThrowsStaleRefusal()
    {
        // Arrange — make one csproj newer than the binlog (a rebuild the binlog no longer reflects).
        using ReplayedSolution replayed = BinlogReplayer.Replay(Fixture.BinlogPath);
        string csproj = CsprojPathOf(replayed.Solution, "MyApp.Domain");
        var original = Snapshot(csproj);
        try
        {
            DateTime newer = File.GetLastWriteTimeUtc(Fixture.BinlogPath).AddHours(1);
            File.SetLastWriteTimeUtc(csproj, newer);
            var store = new BinlogCaptureStore(Fixture.SolutionPath, _cacheRoot);

            // Act
            var ex = Should.Throw<UserErrorException>(() => store.Ingest(replayed.Solution, Fixture.BinlogPath, Fixture.BinlogPath));

            // Assert — the exact dictated refusal naming the newest offending structural file.
            ex.Message.ShouldBe(BinlogCaptureStore.StaleAtIngestMessage(Fixture.BinlogPath, csproj));
        }
        finally
        {
            Restore(csproj, original);
        }
    }

    [Fact]
    public void Ingest_BinlogMissingSolutionProjects_ThrowsMissingCoverageRefusal()
    {
        // Arrange — build ONLY the leaf project (MyApp.Legacy.Billing, which references nothing) into its
        // own binlog: replaying it yields a one-project solution that does not cover the full sln.
        string leafCsproj = Fixture.PathOf("MyApp.Legacy.Billing", "MyApp.Legacy.Billing.csproj");
        string leafBinlog = Path.Combine(_cacheRoot, "billing-only.binlog");
        Directory.CreateDirectory(_cacheRoot);
        DotnetCli.Run(
            $"build \"{leafCsproj}\" -bl:LogFile=\"{leafBinlog}\" --no-incremental --disable-build-servers",
            Fixture.PathOf());

        using ReplayedSolution replayed = BinlogReplayer.Replay(leafBinlog);
        var store = new BinlogCaptureStore(Fixture.SolutionPath, _cacheRoot);

        // The two solution members the leaf binlog does not build.
        var missing = SolutionProjectFileParser.ReadCsprojMembers(Fixture.SolutionPath)
            .Where(p => !p.EndsWith("MyApp.Legacy.Billing.csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();
        missing.Count.ShouldBe(2);

        // Act
        var ex = Should.Throw<UserErrorException>(() => store.Ingest(replayed.Solution, leafBinlog, leafBinlog));

        // Assert
        ex.Message.ShouldBe(BinlogCaptureStore.MissingCoverageMessage(leafBinlog, missing));
    }

    [Fact]
    public void Ingest_SlnfSolution_RefusesEarlyWithSolutionFilterMessage()
    {
        // A store over a .slnf: Ingest must refuse before touching the solution (an empty AdhocWorkspace
        // solution proves it never reaches CollectProjects/coverage, which is where the misleading
        // zero-members "coverage" message used to come from).
        var store = new BinlogCaptureStore(Path.Combine(_cacheRoot, "App.slnf"), _cacheRoot);
        Solution empty = new AdhocWorkspace().CurrentSolution;

        var ex = Should.Throw<UserErrorException>(() => store.Ingest(empty, "build.binlog", "build.binlog"));
        ex.Message.ShouldBe(BinlogCaptureStore.SolutionFilterNotSupportedMessage("App.slnf"));
    }

    [Fact]
    public void Ingest_BinlogHasProjectNotInSolution_ThrowsExtraCoverageRefusal()
    {
        // Arrange — a reduced solution missing the MyApp.Web member; the full binlog builds it anyway.
        string reducedSln = WriteReducedSolutionWithoutWeb();
        try
        {
            using ReplayedSolution replayed = BinlogReplayer.Replay(Fixture.BinlogPath);
            var store = new BinlogCaptureStore(reducedSln, _cacheRoot);
            string webCsproj = CsprojPathOf(replayed.Solution, "MyApp.Web");

            // Act
            var ex = Should.Throw<UserErrorException>(() => store.Ingest(replayed.Solution, Fixture.BinlogPath, Fixture.BinlogPath));

            // Assert — the refusal names the extra project and the reduced solution's file name.
            ex.Message.ShouldBe(BinlogCaptureStore.ExtraCoverageMessage(Fixture.BinlogPath, "MyApp.Reduced.sln", [webCsproj]));
        }
        finally
        {
            File.Delete(reducedSln);
        }
    }

    // ── fully-literal message pins (pin the dictated constants themselves) ────────────────────────────────

    [Fact]
    public void StaleAtIngestMessage_RendersDictatedText()
    {
        BinlogCaptureStore.StaleAtIngestMessage("build.binlog", @"C:\repo\App\App.csproj")
            .ShouldBe("--binlog 'build.binlog' predates 'C:\\repo\\App\\App.csproj' (the build no longer reflects the current tree). Rebuild with -bl and pass the fresh binlog.");
    }

    [Fact]
    public void MissingCoverageMessage_RendersDictatedText()
    {
        BinlogCaptureStore.MissingCoverageMessage("build.binlog", [@"C:\repo\A\A.csproj", @"C:\repo\B\B.csproj"])
            .ShouldBe("--binlog 'build.binlog' does not cover the solution; missing from the binlog:\n  C:\\repo\\A\\A.csproj\n  C:\\repo\\B\\B.csproj\nBuild the whole solution with -bl and pass that binlog.");
    }

    [Fact]
    public void ExtraCoverageMessage_RendersDictatedText()
    {
        BinlogCaptureStore.ExtraCoverageMessage("build.binlog", "App.sln", [@"C:\repo\Extra\Extra.csproj"])
            .ShouldBe("--binlog 'build.binlog' contains projects that are not in 'App.sln':\n  C:\\repo\\Extra\\Extra.csproj\nPass a .binlog produced by building exactly this solution.");
    }

    [Fact]
    public void SolutionFilterNotSupportedMessage_RendersDictatedText()
    {
        BinlogCaptureStore.SolutionFilterNotSupportedMessage("App.slnf")
            .ShouldBe("'App.slnf' is a solution filter (.slnf); build captures require the full solution. Pass the .sln/.slnx instead.");
    }

    [Fact]
    public void StaleNotice_RendersDictatedText()
    {
        BinlogCaptureStore.StaleNotice(@"C:\repo\App\App.csproj")
            .ShouldBe("build capture is stale ('C:\\repo\\App\\App.csproj' no longer matches the capture); running a design-time build instead. Re-capture: rebuild with -bl and re-run with --binlog.");
    }

    [Fact]
    public void UnreadableNotice_RendersDictatedText()
    {
        BinlogCaptureStore.UnreadableNotice
            .ShouldBe("build capture is unreadable; running a design-time build instead. Re-capture: rebuild with -bl and re-run with --binlog.");
    }

    [Fact]
    public void VersionMismatchNotice_RendersDictatedText()
    {
        BinlogCaptureStore.VersionMismatchNotice
            .ShouldBe("build capture was written by a different LoadBearing version; running a design-time build instead. Re-capture: rebuild with -bl and re-run with --binlog.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────

    // Replays the shared full binlog and ingests it against the full solution — the common Arrange.
    private BinlogCaptureStore IngestFullCapture()
    {
        using ReplayedSolution replayed = BinlogReplayer.Replay(Fixture.BinlogPath);
        var store = new BinlogCaptureStore(Fixture.SolutionPath, _cacheRoot);
        store.Ingest(replayed.Solution, Fixture.BinlogPath, Fixture.BinlogPath).ShouldBeTrue();
        return store;
    }

    private static string CsprojPathOf(Solution solution, string projectName)
    {
        Project project = solution.Projects.First(p => p.Name == projectName);
        return Path.GetFullPath(project.FilePath!);
    }

    private static string DocumentPathOf(Solution solution, string projectName, string fileName)
    {
        Document document = solution.Projects
            .First(p => p.Name == projectName).Documents
            .First(d => d.FilePath is not null && Path.GetFileName(d.FilePath) == fileName);
        return Path.GetFullPath(document.FilePath!);
    }

    // Copies MyApp.sln to MyApp.Reduced.sln minus the MyApp.Web project block, backdated below the binlog so
    // the ingest staleness check passes and the coverage check is what fires. Returns the reduced-sln path.
    private string WriteReducedSolutionWithoutWeb()
    {
        var lines = File.ReadAllLines(Fixture.SolutionPath).ToList();
        int webLine = lines.FindIndex(l => l.TrimStart().StartsWith("Project(") && l.Contains("\"MyApp.Web\""));
        lines.RemoveAt(webLine + 1); // the following EndProject
        lines.RemoveAt(webLine); // the Project(...) line itself

        string reducedSln = Fixture.PathOf("MyApp.Reduced.sln");
        File.WriteAllLines(reducedSln, lines);
        File.SetLastWriteTimeUtc(reducedSln, File.GetLastWriteTimeUtc(Fixture.BinlogPath).AddMinutes(-5));
        return reducedSln;
    }

    private void MutateManifest(Action<JsonObject> mutate)
    {
        string path = CacheLocations.CaptureManifestPath(Fixture.SolutionPath, _cacheRoot);
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        mutate(root);
        File.WriteAllText(path, root.ToJsonString());
    }

    private static (byte[] bytes, DateTime mtime) Snapshot(string path)
    {
        return (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path));
    }

    private static void Restore(string path, (byte[] bytes, DateTime mtime) snapshot)
    {
        File.WriteAllBytes(path, snapshot.bytes);
        File.SetLastWriteTimeUtc(path, snapshot.mtime);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup of the throwaway cache root
        }
    }
}