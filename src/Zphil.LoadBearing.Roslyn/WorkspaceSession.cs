using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     A host-managed, long-lived solution that stays correct across many reads by reconciling against
///     disk at the start of each read — the warm counterpart to the one-shot <see cref="WorkspaceLoader" />.
///     One session owns one <see cref="LoadedSolution" /> (and therefore one <c>MSBuildWorkspace</c> and
///     its out-of-process BuildHost) plus a private, forward-only snapshot chain; callers receive
///     immutable <see cref="WorkspaceSnapshot" />s and never touch the workspace directly.
/// </summary>
/// <remarks>
///     <para>
///         <b>Lifetime contract.</b> <see cref="WorkspaceLoader" /> is the one-shot primitive: a check or
///         render run opens a workspace, reads once, and disposes, so it needs no reconcile machinery and
///         the CLI and xUnit adapter keep that restored-solution staleness contract. This type is the
///         other lifetime — a warm MCP server that answers many tool calls against one loaded solution.
///         It is lazy: the first <see cref="GetCurrentAsync" /> performs the full load (no eager warmup),
///         which preserves per-call error-text parity with the cold path.
///     </para>
///     <para>
///         <b>Correct at call time, by construction — no watcher.</b> The solution only has to be correct
///         <em>at</em> a tool call, so each call runs a per-call reconcile sweep (cheapest checks first)
///         instead of a background <see cref="System.IO.FileSystemWatcher" />: a structural stat sweep
///         (solution file, every csproj, <c>Directory.Build.props</c>/<c>.targets</c>/<c>global.json</c>
///         probe chains with absence recorded, per-project <c>obj/project.assets.json</c>), a cone scan
///         for newly-added <c>*.cs</c> under each project directory, then a per-document stat sweep with
///         the racy-window semantics of <see cref="FileFreshness" />. Any structural delta, any new or
///         deleted source file disposes the workspace and reloads wholesale; an in-place content edit is
///         folded into the snapshot chain via
///         <see cref="Solution.WithDocumentText(DocumentId,SourceText,PreservationMode)" />.
///     </para>
///     <para>
///         <b>Read-only, so no write-back.</b> Unlike an editor server, the carried snapshot is never
///         applied back to the workspace (it is already a forked, unresolved-reference-stripped solution —
///         see <see cref="SolutionExtensions.StripUnresolvedReferences" />). Content edits mint a new
///         private snapshot rather than mutating the workspace, so there is no pending-update or drain
///         machinery — just the immutable chain.
///     </para>
///     <para>
///         <b>Concurrency.</b> All mutation of loaded state runs under a single <see cref="SemaphoreSlim" />,
///         so sweeps and reloads serialize. Readers share immutable snapshots; a consumer still holding a
///         pre-reload snapshot is safe because a Roslyn <see cref="Solution" /> survives the disposal of the
///         workspace that produced it.
///     </para>
/// </remarks>
public sealed class WorkspaceSession : IAsyncDisposable
{
    private static readonly string[] StructuralProbeFileNames =
        ["Directory.Build.props", "Directory.Build.targets", "global.json"];

    private readonly Action<string>? diagnosticSink;

    // Known-document fingerprints, keyed by canonical full path. Doubles as the cone-scan membership set:
    // any *.cs found under a project directory whose path is absent here is a newly-added file.
    private readonly Dictionary<string, FileFreshness> documentFingerprints = new(StringComparer.OrdinalIgnoreCase);

    // Canonical full path ⇒ the document(s) at that path, captured at load and keyed identically to
    // documentFingerprints. Resolving an edited file to its DocumentId(s) through this map avoids
    // round-tripping through Roslyn's own path matching, whose spelling need not equal our normalized key.
    // DocumentIds survive WithDocumentText, so the map stays valid across in-place edits within one load.
    private readonly Dictionary<string, List<DocumentId>> documentIds = new(StringComparer.OrdinalIgnoreCase);

    // The one gate that serializes every sweep, reload, and edit against concurrent callers.
    private readonly SemaphoreSlim gate = new(1, 1);

    // Project directories (from the loaded solution) scanned for newly-added source files.
    private readonly HashSet<string> projectDirectories = new(StringComparer.OrdinalIgnoreCase);

    // Per-project (by name) edit counters within the current generation: reset (seeded to 0 for every C#
    // project) on each full load, bumped when the reconcile sweep rewrites one of a project's documents.
    // Stamped as an immutable copy onto each snapshot so a consumer can diff two snapshots' maps to learn
    // exactly which projects' bytes changed between them.
    private readonly Dictionary<string, int> projectEditVersions = new(StringComparer.Ordinal);

    // Structural-file fingerprints, keyed by canonical full path. Absent probe-chain entries are recorded
    // with Exists = false so a file that later appears trips the sweep.
    private readonly Dictionary<string, FileFreshness> structuralFingerprints = new(StringComparer.OrdinalIgnoreCase);

    // The private snapshot chain: the current (possibly edited) forked solution.
    private Solution? current;

    private int disposed;

    // The load generation, bumped on every full (re)load and stamped onto each snapshot. A session-scoped
    // consumer (the incremental fragment store, Phase 12 D2) flushes when it changes; within a generation it
    // reuses its work. Starts at 0; the first load makes it 1, so a never-loaded generation never aliases one.
    private long generation;

    // Workspace-load diagnostics of the current generation, carried onto every snapshot it produces.
    private IReadOnlyList<string> loadDiagnostics = [];

    // The owning workspace of the current load generation. Null until the first load, and between a
    // reset-to-unloaded and the next successful load.
    private LoadedSolution? loaded;

    // Canonical path of the currently-loaded solution; drives the "path changed ⇒ full load" branch.
    private string? loadedSolutionPath;

    // The cached immutable snapshot returned to callers. Reference-stable while nothing changes, so a
    // no-op reconcile hands back the very same instance.
    private WorkspaceSnapshot? snapshot;

    /// <summary>
    ///     Creates an unloaded session. The workspace is opened lazily on the first
    ///     <see cref="GetCurrentAsync" />.
    /// </summary>
    /// <param name="diagnosticSink">
    ///     Optional operational-log sink for reconcile events the caller may want to surface (per-file read
    ///     failures, reload triggers). Distinct from a snapshot's
    ///     <see cref="WorkspaceSnapshot.Diagnostics" />, which carries workspace-load failures. Null (the
    ///     default) swallows these messages.
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
    ///     Disposes the owning workspace and releases the session. Idempotent — a second call is a no-op —
    ///     and bounded: it waits only a short interval for any in-flight sweep to release the gate before
    ///     tearing down, so a wedged sweep cannot block process shutdown.
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
    ///     Returns the solution for <paramref name="solutionPath" />, reconciled against disk as of this
    ///     call. The first call (or a call with a different path) performs a full load; subsequent calls
    ///     run the reconcile sweep and reload only when a structural change, an added file, or a deleted
    ///     document demands it. An in-place edit is folded into a fresh snapshot; an unchanged tree returns
    ///     the same snapshot instance.
    /// </summary>
    /// <param name="solutionPath">Absolute path to the <c>.sln</c>/<c>.slnx</c> to load.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An immutable snapshot: the reconciled <see cref="Solution" /> and its load diagnostics.</returns>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <remarks>
    ///     A load failure resets the session to unloaded and rethrows, so the next call retries cleanly
    ///     from a fresh workspace rather than serving a half-initialized one.
    /// </remarks>
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
        loadDiagnostics = [];
        documentFingerprints.Clear();
        documentIds.Clear();
        structuralFingerprints.Clear();
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
        loadDiagnostics = collected;
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
        if (StructuralSweepDetectsChange() || ConeScanDetectsNewFile())
        {
            await LoadFreshAsync(loadedSolutionPath!, ct).ConfigureAwait(false);
            return;
        }

        bool needsReload = await ReconcileDocumentsAsync(ct).ConfigureAwait(false);
        if (needsReload)
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
    ///     Enumerates <c>*.cs</c> under each project directory (skipping <c>bin</c>/<c>obj</c>) and reports
    ///     whether any file is not already a known document — the add case an mtime sweep cannot see, since
    ///     an SDK-glob add touches no MSBuild file.
    /// </summary>
    private bool ConeScanDetectsNewFile()
    {
        foreach (string projectDirectory in projectDirectories)
        {
            if (!Directory.Exists(projectDirectory)) continue;

            foreach (string file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildArtifact(projectDirectory, file)) continue;

                string full = Path.GetFullPath(file);
                if (documentFingerprints.ContainsKey(full)) continue;

                diagnosticSink?.Invoke($"New source file detected, reloading: {full}");
                return true;
            }
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

            if (!documentIds.TryGetValue(path, out var ids) || ids.Count == 0)
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
    ///     Mints a snapshot of the current solution stamped with this generation and an immutable copy of the
    ///     per-project edit-version map, so a consumer holding the snapshot keeps a frozen view even as later
    ///     sweeps keep bumping the live map.
    /// </summary>
    private WorkspaceSnapshot MintSnapshot()
    {
        return new WorkspaceSnapshot(current!, loadDiagnostics)
        {
            Generation = generation,
            ProjectEditVersions = new Dictionary<string, int>(projectEditVersions, StringComparer.Ordinal)
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
    ///     same-name <see cref="Project" />s collapse onto one counter, so the version keys align with the
    ///     name-keyed fragment store; the absolute count is irrelevant — only that it changes when bytes did.
    /// </summary>
    private void BumpEditVersions(IReadOnlyList<DocumentId> ids)
    {
        foreach (DocumentId id in ids)
            if (current!.GetProject(id.ProjectId)?.Name is { } name)
                projectEditVersions[name] = projectEditVersions.GetValueOrDefault(name) + 1;
    }

    /// <summary>
    ///     Records the load-time fingerprints: every document (unverified, so the first sweep content-checks
    ///     it once and promotes it) plus the structural set (solution, csprojs, per-project assets, and the
    ///     props/targets/global.json probe chain from each project directory up to the solution directory,
    ///     absence included).
    /// </summary>
    private void RecordAllFingerprints(string solutionPath, Solution solution)
    {
        string solutionDirectory = Path.GetDirectoryName(solutionPath)!;

        foreach (Document document in solution.Projects.SelectMany(p => p.Documents))
        {
            if (document.FilePath is null) continue;

            string full = Path.GetFullPath(document.FilePath);
            documentFingerprints[full] = FileFreshness.CaptureUnverified(full);
            if (!documentIds.TryGetValue(full, out var ids))
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
            RecordStructural(Path.Combine(projectDirectory, "obj", "project.assets.json"));
        }

        foreach (string projectDirectory in projectDirectories)
        foreach (string ancestor in AncestorsUpTo(projectDirectory, solutionDirectory))
        foreach (string probe in StructuralProbeFileNames)
            RecordStructural(Path.Combine(ancestor, probe));
    }

    private void RecordStructural(string path)
    {
        structuralFingerprints[Path.GetFullPath(path)] = FileFreshness.Capture(path);
    }

    /// <summary>
    ///     Yields <paramref name="startDirectory" /> and each ancestor up to and including
    ///     <paramref name="stopDirectory" />. If the start is not under the stop (an out-of-tree project),
    ///     the walk terminates at the filesystem root instead — harmless extra absent probes.
    /// </summary>
    private static IEnumerable<string> AncestorsUpTo(string startDirectory, string stopDirectory)
    {
        string? directory = startDirectory;
        while (directory is not null)
        {
            yield return directory;
            if (PathEquals(directory, stopDirectory)) yield break;
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static bool IsBuildArtifact(string projectDirectory, string file)
    {
        string relative = Path.GetRelativePath(projectDirectory, file).Replace('\\', '/');
        return relative.Split('/').Any(segment => segment is "bin" or "obj");
    }

    /// <summary>
    ///     Pulls every document's text into a concrete in-memory <see cref="SourceText" />, anchoring the
    ///     reconcile baseline. MSBuildWorkspace backs documents with lazy file-text loaders, so an unforced
    ///     <see cref="TextDocument.GetTextAsync" /> during a later sweep would re-read disk — comparing
    ///     on-disk content against itself and masking every external edit. Reading each document once at load
    ///     (the on-disk content is the loaded content at this instant) fixes the version to compare against,
    ///     and it is the same work the caller's extraction would do, so it is not double-read.
    /// </summary>
    private static async Task<Solution> MaterializeDocumentTextsAsync(Solution solution, CancellationToken ct)
    {
        Solution result = solution;
        foreach (DocumentId id in solution.Projects.SelectMany(p => p.DocumentIds).ToList())
        {
            ct.ThrowIfCancellationRequested();

            Document? document = result.GetDocument(id);
            if (document?.FilePath is null) continue;

            SourceText text = await document.GetTextAsync(ct).ConfigureAwait(false);
            result = result.WithDocumentText(id, text);
        }

        return result;
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
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}