using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     A loaded solution kept alive for a host that reads it many times: a test adapter, a long-running
///     tool, a server. Each <see cref="GetCurrentAsync" /> reconciles the solution against disk and hands
///     back an immutable <see cref="WorkspaceSnapshot" />. The session owns the MSBuild workspace and the
///     out-of-process build host behind it, so dispose it when the host shuts down. For a single read, use
///     <see cref="WorkspaceLoader" /> instead.
/// </summary>
/// <remarks>
///     <para>
///         Nothing is loaded until the first <see cref="GetCurrentAsync" />, which pays the whole solution load; later
///         calls pay only the reconcile. A structural change (the solution file, a project file, a
///         <c>Directory.Build.props</c>, <c>Directory.Build.targets</c> or <c>Directory.Packages.props</c> at or above
///         a project, a <c>global.json</c>, a restore assets file) or a source file added or deleted reloads the
///         solution wholesale. An edit to a file already in the solution is folded into a fresh snapshot, and an
///         unchanged tree hands back the very snapshot instance the previous call returned.
///     </para>
///     <para>
///         It reads and never writes: no file is touched, and no snapshot is applied back to the workspace.
///         Pointing a session at a working tree somebody else is editing is the expected use.
///     </para>
///     <para>
///         Calls serialize, so several callers asking at once share one load rather than starting several. A
///         snapshot already handed out stays readable while a later call reloads, and after the session is
///         disposed, because a Roslyn <see cref="Solution" /> outlives the workspace that produced it.
///     </para>
/// </remarks>
// The first read pays the whole load rather than the constructor doing it eagerly, so a load failure
// reaches the caller with the same text the one-shot WorkspaceLoader produces; per-call error-text
// parity between the warm and the cold path is pinned.
public sealed class WorkspaceSession : IAsyncDisposable
{
    private readonly Action<string>? diagnosticSink;

    // Known-document fingerprints, keyed by canonical full path. Covers only the project's COMPILED documents,
    // so it must not double as the cone-scan membership set — a *.cs on disk but excluded from compilation
    // (<Compile Remove>, a None item) is absent here yet is not a new file. That role is knownConeFiles.
    private readonly Dictionary<string, FileFreshness> documentFingerprints = new(PathComparison.Comparer);

    // Canonical full path ⇒ the document(s) at that path, captured at load and keyed identically to
    // documentFingerprints. Resolving an edited file to its DocumentId(s) through this map avoids
    // round-tripping through Roslyn's own path matching, whose spelling need not equal our normalized key.
    // DocumentIds survive WithDocumentText, so the map stays valid across in-place edits within one load.
    private readonly Dictionary<string, List<DocumentId>> documentIds = new(PathComparison.Comparer);

    // The one gate that serializes every sweep, reload, and edit against concurrent callers.
    private readonly SemaphoreSlim gate = new(1, 1);

    // Every *.cs the cone scan would find under the loaded project directories (bin/obj excluded), snapshotted
    // at load — the true cone-scan membership set. Only a file the scan finds that is absent here counts as a
    // newly-added source file; a compiled document and an excluded stray both live here, so neither trips it.
    private readonly HashSet<string> knownConeFiles = new(PathComparison.Comparer);

    // Project directories (from the loaded solution) scanned for newly-added source files.
    private readonly HashSet<string> projectDirectories = new(PathComparison.Comparer);

    // Per-project (by name) edit counters within the current generation: reset (seeded to 0 for every C#
    // project) on each full load, bumped when the reconcile sweep rewrites one of a project's documents.
    // Stamped as an immutable copy onto each snapshot so a consumer can diff two snapshots' maps to learn
    // exactly which projects' bytes changed between them.
    private readonly Dictionary<string, int> projectEditVersions = new(StringComparer.Ordinal);

    // Structural-file fingerprints, keyed by canonical full path. Absent probe-chain entries are recorded
    // with Exists = false so a file that later appears trips the sweep.
    private readonly Dictionary<string, FileFreshness> structuralFingerprints = new(PathComparison.Comparer);

    // The private snapshot chain: the current (possibly edited) forked solution.
    private Solution? current;

    private int disposed;

    // The load generation, bumped on every full (re)load and stamped onto each snapshot. A session-scoped
    // consumer (the incremental fragment store) flushes when it changes; within a generation it
    // reuses its work. Starts at 0; the first load makes it 1, so a never-loaded generation never aliases one.
    private long generation;

    // The current generation's load verdict — the diagnostics that render beside the three project lists that
    // decide — carried whole onto every snapshot the generation produces. One field rather than four because
    // one load writes them and one mint reads them: kept apart, they are four chances to describe two
    // different loads. Generation-scoped, and safely so: a repairing restore writes an assets file the
    // reconcile sweep already stamps — present or absent, so its first appearance counts too — which forces a
    // full reload rather than letting a verdict go stale inside a generation.
    private WorkspaceDiagnostics loadVerdict = WorkspaceDiagnostics.None;

    // The owning workspace of the current load generation. Null until the first load, and between a
    // reset-to-unloaded and the next successful load.
    private LoadedSolution? loaded;

    // Canonical path of the currently-loaded solution; drives the "path changed ⇒ full load" branch.
    private string? loadedSolutionPath;

    // The cached immutable snapshot returned to callers. Reference-stable while nothing changes, so a
    // no-op reconcile hands back the very same instance.
    private WorkspaceSnapshot? snapshot;

    // Per-project target frameworks of the current load generation, carried onto every snapshot it produces.
    // ProjectIds survive WithDocumentText, so one load's map stays valid for every edit folded into it.
    private IReadOnlyDictionary<ProjectId, string> targetFrameworks = TargetFrameworkMaps.None;

    /// <summary>
    ///     Creates a session with nothing loaded. The first <see cref="GetCurrentAsync" /> opens the workspace
    ///     and loads the solution.
    /// </summary>
    /// <param name="diagnosticSink">
    ///     Optional sink for the session's own running commentary: why a call decided to reload, and any file
    ///     the reconcile could not read. Distinct from a snapshot's
    ///     <see cref="WorkspaceSnapshot.Diagnostics" />, which carries the failures of the load itself. Null,
    ///     the default, discards these messages.
    /// </param>
    public WorkspaceSession(Action<string>? diagnosticSink = null)
    {
        this.diagnosticSink = diagnosticSink;
    }

    /// <summary>
    ///     The number of on-disk content reads performed by the per-document reconcile sweep. Zero in the
    ///     steady state (a provably-unchanged file is trusted on stat alone and never re-read). Internal
    ///     test observable; never consulted in production.
    /// </summary>
    internal long SweepContentReads { get; private set; }

    /// <summary>
    ///     The number of full solution loads this session has performed — the initial load plus every
    ///     structural/add/delete-triggered reload. Internal test observable; a burst of concurrent callers
    ///     against an unchanged tree must leave this at 1.
    /// </summary>
    internal long FullReloadCount { get; private set; }

    /// <summary>
    ///     Disposes the MSBuild workspace and its out-of-process build host and releases the session. A second
    ///     call is a no-op. It waits briefly for a call already in flight to finish before tearing down, so a
    ///     wedged read cannot hold up process shutdown; snapshots already handed out stay readable afterwards.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;

        // Bounded acquire: on shutdown, disposing the workspace is the safer outcome even if a sweep is
        // still holding the gate, so proceed after the wait either way. 12 s matches the pre-sized MCP
        // disposer budget (ServerShutdown).
        bool acquired = await gate.WaitAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
        try
        {
            loaded?.Dispose();
            loaded = null;
            current = null;
            snapshot = null;
        }
        finally
        {
            if (acquired) gate.Release();
            gate.Dispose();
        }
    }

    /// <summary>
    ///     Returns the solution at <paramref name="solutionPath" />, reconciled against disk as of this call.
    ///     The first call, and any call naming a different path, loads the solution in full; later calls stat
    ///     what they already know and reload only when a structural change, an added file or a deleted file
    ///     demands it. An edit to a file already in the solution is folded into a fresh snapshot, and an
    ///     unchanged tree returns the same snapshot instance as the call before. A load that fails leaves the
    ///     session unloaded and throws, so the next call starts again from a fresh workspace rather than
    ///     serving a half-loaded one.
    /// </summary>
    /// <param name="solutionPath">
    ///     Absolute path to the <c>.sln</c>/<c>.slnx</c> to load, or to a <c>.slnf</c> filter over one.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An immutable snapshot: the reconciled <see cref="Solution" /> and the load's diagnostics.</returns>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="UserErrorException">
    ///     The <c>.slnf</c> filter at <paramref name="solutionPath" /> could not be read. Its message is
    ///     written for the person who ran the tool and is complete on its own.
    /// </exception>
    public async Task<WorkspaceSnapshot> GetCurrentAsync(string solutionPath, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        string normalizedPath = Path.GetFullPath(solutionPath);

        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);

            if (loaded is null || !PathEquals(loadedSolutionPath, normalizedPath))
                await LoadFreshAsync(normalizedPath, ct).ConfigureAwait(false);
            else
                await ReconcileAsync(ct).ConfigureAwait(false);

            return snapshot!;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    ///     Disposes any current generation and opens a fresh workspace. State is reset to unloaded up front
    ///     so a throwing load leaves nothing half-initialized: the next call retries from scratch.
    /// </summary>
    private async Task LoadFreshAsync(string solutionPath, CancellationToken ct)
    {
        LoadedSolution? previous = loaded;
        loaded = null;
        current = null;
        loadedSolutionPath = null;
        snapshot = null;
        loadVerdict = WorkspaceDiagnostics.None;
        targetFrameworks = TargetFrameworkMaps.None;
        documentFingerprints.Clear();
        documentIds.Clear();
        structuralFingerprints.Clear();
        knownConeFiles.Clear();
        projectDirectories.Clear();
        projectEditVersions.Clear();

        previous?.Dispose();

        List<string> collected = [];
        var collectedLock = new object();

        LoadedSolution freshlyLoaded = await WorkspaceLoader.LoadAsync(solutionPath, CollectDiagnostic, ct).ConfigureAwait(false);
        Solution materialized = await MaterializeDocumentTextsAsync(freshlyLoaded.Solution, ct).ConfigureAwait(false);

        loaded = freshlyLoaded;
        current = materialized;
        loadedSolutionPath = solutionPath;
        loadVerdict = freshlyLoaded.LoadDiagnosticsWith(collected);
        targetFrameworks = freshlyLoaded.TargetFrameworks;
        generation++;
        SeedEditVersions(materialized);
        RecordAllFingerprints(solutionPath, materialized);
        snapshot = MintSnapshot();
        FullReloadCount++;
        return;

        // MSBuildWorkspace can raise workspace-failure diagnostics from BuildHost callback threads while
        // the open is in flight, so synchronize the collector even though the load itself is gate-serialized.
        void CollectDiagnostic(string message)
        {
            lock (collectedLock)
            {
                collected.Add(message);
            }
        }
    }

    /// <summary>
    ///     Runs the reconcile sweep cheapest-first over the loaded solution and either folds in-place edits
    ///     into the snapshot chain or, on any structural/add/delete signal, disposes and reloads wholesale.
    /// </summary>
    private async Task ReconcileAsync(CancellationToken ct)
    {
        // Cheapest-first is the short-circuit itself: the document sweep is the only arm that reads file
        // content, so it must not run once a reload is already decided.
        if (StructuralSweepDetectsChange() || ConeScanDetectsNewFile() || await ReconcileDocumentsAsync(ct).ConfigureAwait(false))
            await LoadFreshAsync(loadedSolutionPath!, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Stats every recorded structural file (solution, csprojs, props/targets/global.json probe chains,
    ///     per-project assets) and reports whether any differs from its recorded fingerprint — including an
    ///     absent probe file that has since appeared.
    /// </summary>
    private bool StructuralSweepDetectsChange()
    {
        foreach ((string path, FileFreshness recorded) in structuralFingerprints)
        {
            FileFreshness now = FileFreshness.Capture(path);
            if (recorded.MatchesStat(now)) continue;

            diagnosticSink?.Invoke($"Structural change detected, reloading: {path}");
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Rescans each project directory's <see cref="Caching.ProjectCone">cone</see> and reports whether any
    ///     <c>*.cs</c> is not in the load-time <see cref="knownConeFiles" /> snapshot — the add case an mtime
    ///     sweep cannot see, since an SDK-glob add touches no MSBuild file. Comparing against the recorded cone
    ///     (not <see cref="documentFingerprints" />, the compiled set) means an excluded stray already on disk
    ///     at load is not mistaken for a perpetual add; only a file that appeared since load reloads.
    /// </summary>
    private bool ConeScanDetectsNewFile()
    {
        foreach (string projectDirectory in projectDirectories)
        foreach (string file in ProjectCone.Enumerate(projectDirectory))
        {
            if (knownConeFiles.Contains(file)) continue;

            diagnosticSink?.Invoke($"New source file detected, reloading: {file}");
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Stats every known document with the racy-window semantics of <see cref="FileFreshness" />:
    ///     provably-unchanged files are trusted on stat alone; changed or racy files are content-verified
    ///     (short-circuiting when the bytes match) and otherwise folded into the snapshot via
    ///     <see cref="Solution.WithDocumentText(DocumentId,SourceText,PreservationMode)" />. A missing
    ///     document signals a full reload; a per-file read failure is logged and skipped, leaving that
    ///     entry stale-fingerprinted for the next call so it never poisons the rest of the sweep.
    /// </summary>
    /// <returns><c>true</c> when a document has gone missing and the caller must reload wholesale.</returns>
    private async Task<bool> ReconcileDocumentsAsync(CancellationToken ct)
    {
        // Snapshot the key set: the loop reassigns documentFingerprints entries in place.
        List<string> paths = [.. documentFingerprints.Keys];

        foreach (string path in paths)
        {
            ct.ThrowIfCancellationRequested();

            // Snapshot the fingerprint BEFORE reading, so the value we record reflects the version we
            // actually load; a write that interleaves with the read then can't be masked.
            FileFreshness beforeRead = FileFreshness.Capture(path);
            if (!beforeRead.Exists) return true; // deleted document ⇒ full reload

            if (documentFingerprints[path].CanTrust(beforeRead)) continue; // provably unchanged

            SourceText text;
            try
            {
                text = await ReadTextAsync(path, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leave the entry stale-fingerprinted (retried next call) rather than poison the sweep.
                diagnosticSink?.Invoke($"Skipping unreadable file during reconcile: {path} ({ex.Message})");
                continue;
            }

            SweepContentReads++;

            if (!documentIds.TryGetValue(path, out List<DocumentId>? ids) || ids.Count == 0)
            {
                // Path recorded but no document maps to it (defensive): re-fingerprint and move on.
                documentFingerprints[path] = beforeRead;
                continue;
            }

            SourceText existing = await current!.GetDocument(ids[0])!.GetTextAsync(ct).ConfigureAwait(false);
            if (existing.ContentEquals(text))
            {
                // Same bytes (an mtime-only touch): keep the snapshot, promote the fingerprint.
                documentFingerprints[path] = beforeRead;
                continue;
            }

            Solution updated = current;
            foreach (DocumentId id in ids)
                updated = updated.WithDocumentText(id, text);

            current = updated;
            documentFingerprints[path] = beforeRead;
            BumpEditVersions(ids);
            snapshot = MintSnapshot();
        }

        return false;
    }

    /// <summary>
    ///     Mints a snapshot of the current solution stamped with this generation, the load's per-project
    ///     target frameworks and failed projects, and an immutable copy of the per-project edit-version map,
    ///     so a consumer holding the snapshot keeps a frozen view even as later sweeps keep bumping the live
    ///     map.
    /// </summary>
    private WorkspaceSnapshot MintSnapshot()
    {
        return new WorkspaceSnapshot(current!, loadVerdict.LoadFailures)
        {
            Generation = generation,
            FailedProjects = loadVerdict.FailedProjects,
            UncheckedProjects = loadVerdict.UncheckedProjects,
            RestoreFailedProjects = loadVerdict.RestoreFailedProjects,
            UnsupportedProjects = loadVerdict.UnsupportedProjects,
            ProjectEditVersions = new Dictionary<string, int>(projectEditVersions, StringComparer.Ordinal),
            TargetFrameworks = targetFrameworks
        };
    }

    /// <summary>
    ///     Reseeds the edit-version map for a fresh generation: every C# project starts at 0, fixing the map's
    ///     key set for the generation's life so two same-generation snapshots differ only where bytes changed.
    /// </summary>
    private void SeedEditVersions(Solution solution)
    {
        projectEditVersions.Clear();
        foreach (Project project in solution.Projects)
            if (project.Language == LanguageNames.CSharp)
                projectEditVersions[project.Name] = 0;
    }

    /// <summary>
    ///     Bumps the edit counter of every project owning one of the just-rewritten documents, resolving each
    ///     <see cref="DocumentId" /> to its project by name. A multi-target-framework project's several
    ///     <see cref="Project" />s do share one name — <see cref="SolutionExtensions.NormalizeProjectNames" />
    ///     saw to that at the load boundary — so they collapse onto one counter and the version keys align
    ///     with the name-keyed fragment store; the absolute count is irrelevant, only that it changes when
    ///     bytes did.
    /// </summary>
    private void BumpEditVersions(IReadOnlyList<DocumentId> ids)
    {
        foreach (DocumentId id in ids)
            if (current!.GetProject(id.ProjectId)?.Name is { } name)
                projectEditVersions[name] = projectEditVersions.GetValueOrDefault(name) + 1;
    }

    /// <summary>
    ///     Records the load-time fingerprints: every document (unverified, so the first sweep content-checks
    ///     it once and promotes it), the whole-cone <c>*.cs</c> membership snapshot each project directory
    ///     globs (so the cone scan tells a real add from an always-present excluded stray), plus the structural
    ///     set (solution, csprojs, per-project assets, and the props/targets/global.json probe chain from each
    ///     project directory up to the filesystem root, absence included).
    /// </summary>
    /// <remarks>
    ///     The structural chain is recorded per <see cref="Project" /> rather than per project directory,
    ///     because where the restore assets file lands follows the project's own evaluated output paths and
    ///     not its directory. Recording is idempotent, so a multi-target-framework project's several
    ///     <see cref="Project" />s contributing the same paths costs nothing.
    /// </remarks>
    private void RecordAllFingerprints(string solutionPath, Solution solution)
    {
        foreach (Document document in solution.Projects.SelectMany(p => p.Documents))
        {
            if (document.FilePath is null) continue;

            string full = Path.GetFullPath(document.FilePath);
            documentFingerprints[full] = FileFreshness.CaptureUnverified(full);
            if (!documentIds.TryGetValue(full, out List<DocumentId>? ids))
            {
                ids = [];
                documentIds[full] = ids;
            }

            ids.Add(document.Id);
        }

        RecordStructural(solutionPath);

        foreach (Project project in solution.Projects)
        {
            if (project.FilePath is null) continue;

            string projectFile = Path.GetFullPath(project.FilePath);
            RecordStructural(projectFile);

            string projectDirectory = Path.GetDirectoryName(projectFile)!;
            projectDirectories.Add(projectDirectory);

            foreach (string path in ProjectCone.StructuralPaths(
                         projectDirectory, project.OutputFilePath, project.CompilationOutputInfo.AssemblyPath))
                RecordStructural(path);
        }

        foreach (string projectDirectory in projectDirectories)
        foreach (string coneFile in ProjectCone.Enumerate(projectDirectory))
            knownConeFiles.Add(coneFile);
    }

    private void RecordStructural(string path)
    {
        structuralFingerprints[Path.GetFullPath(path)] = FileFreshness.Capture(path);
    }

    /// <summary>
    ///     Pulls every document's text into a concrete in-memory <see cref="SourceText" />, anchoring the
    ///     reconcile baseline. MSBuildWorkspace backs documents with lazy file-text loaders, so an unforced
    ///     <see cref="TextDocument.GetTextAsync" /> during a later sweep would re-read disk — comparing
    ///     on-disk content against itself and masking every external edit. Reading each document once at load
    ///     (the on-disk content is the loaded content at this instant) fixes the version to compare against,
    ///     and it is the same work the caller's extraction would do, so it is not double-read.
    /// </summary>
    /// <remarks>
    ///     Read first, fork after. Every text is taken from the one solution the caller handed in — a Roslyn
    ///     <see cref="Solution" /> is immutable, so the reads are safe together and overlap rather than
    ///     queueing — and only then are they folded in by a tight run of
    ///     <see cref="Solution.WithDocumentText(DocumentId,SourceText,PreservationMode)" />. Interleaving the
    ///     two made every read walk a solution one fork deeper than the last, for a result identical to this one.
    /// </remarks>
    private static async Task<Solution> MaterializeDocumentTextsAsync(Solution solution, CancellationToken ct)
    {
        List<DocumentId> ids = solution.Projects
            .SelectMany(project => project.DocumentIds)
            .ToList();

        List<Task<SourceText?>> reads = ids
            .Select(id => ReadLoadedTextAsync(solution, id, ct))
            .ToList();

        SourceText?[] texts = await Task.WhenAll(reads).ConfigureAwait(false);

        Solution result = solution;
        for (var index = 0; index < ids.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            if (texts[index] is { } text) result = result.WithDocumentText(ids[index], text);
        }

        return result;
    }

    // A document with no file path is not on disk, so no fingerprint will ever compare against it: null asks
    // the caller to leave it on its loader rather than fold a text in.
    private static async Task<SourceText?> ReadLoadedTextAsync(Solution solution, DocumentId id, CancellationToken ct)
    {
        Document? document = solution.GetDocument(id);
        if (document?.FilePath is null) return null;

        return await document.GetTextAsync(ct).ConfigureAwait(false);
    }

    private static async Task<SourceText> ReadTextAsync(string path, CancellationToken ct)
    {
        // Read via a stream so SourceText detects the encoding (BOM-aware, UTF-8 default), matching how
        // the workspace decoded the file on load. FileShare.ReadWrite tolerates a concurrent writer.
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        buffer.Position = 0;
        return SourceText.From(buffer);
    }

    private static bool PathEquals(string? left, string? right)
    {
        return string.Equals(left, right, PathComparison.Comparison);
    }
}
