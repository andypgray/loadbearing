namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The persisted extraction cache as a single serializable document: a self-describing manifest plus
///     every <see cref="CodebaseFragment" /> the last workspace-loaded run extracted. One
///     <see cref="ExtractionCacheStore" /> reads, validates, and (re)writes exactly one of these to one
///     atomic file (Phase 11 WP6). The manifest is deliberately self-contained — it records every input
///     path and its stamp so validation needs no MSBuild and no <see cref="CodebaseFragment" /> inspection.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why the stamps are our own, not <see cref="FileFreshness" />.</b> A stamp is durable on-disk
///         data; <see cref="FileFreshness.RecordedAtUtc" /> is a runtime notion (the wall-clock instant of a
///         capture) that has no meaning across process boundaries. So the racy-window decision is frozen at
///         write time into <see cref="FileStamp.Promoted" /> instead, and validation reconstitutes a
///         <see cref="FileFreshness" /> only for the pure stat comparison (<see cref="FileFreshness.MatchesStat" />).
///     </para>
///     <para>
///         <b>No tamper story.</b> Unlike a baseline file (which carries a digest and treats a hand edit as
///         loud tamper), this cache is disposable local derived data. A garbled, truncated, or hand-edited
///         file degrades to a parse-error <em>miss</em> and the run falls back to the cold path — never an
///         exception, never a wrong answer. See <see cref="ExtractionCacheStore" />.
///     </para>
/// </remarks>
internal sealed record CacheManifest(
    int SchemaVersion,
    string ToolVersion,
    IReadOnlyList<FileStamp> StructuralStamps,
    IReadOnlyList<ProjectCacheEntry> Projects,
    IReadOnlyList<SpecResolutionRecord> SpecResolutions,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<CodebaseFragment> Fragments);

/// <summary>
///     A file's recorded existence and content fingerprint at cache-write time — the durable, cross-process
///     counterpart of a <see cref="FileFreshness" /> capture. Used for both the structural set (solution,
///     csprojs, <c>Directory.Build.*</c>/<c>global.json</c> probe-chain entries, per-project
///     <c>project.assets.json</c>) and a project's source documents.
/// </summary>
/// <param name="Path">The absolute, OS-native path, stored verbatim.</param>
/// <param name="Exists">
///     Whether the file existed at capture. Recorded even when <c>false</c> (an absent probe-chain entry):
///     an absent-then-appearing file is an existence flip validation must catch.
/// </param>
/// <param name="LastWriteTimeUtcTicks">The last-write time in UTC ticks, or 0 when absent.</param>
/// <param name="Length">The byte length, or 0 when absent.</param>
/// <param name="Sha256">The lowercase-hex SHA-256 of the file's bytes, or null when absent or unreadable.</param>
/// <param name="Promoted">
///     Whether this stamp can be trusted on stat-equality alone — the frozen racy-window verdict
///     (<see cref="FileFreshness.IsPromoted" /> at capture). When false, validation must re-hash even on a
///     stat match, because a same-tick write could have shared the recorded mtime.
/// </param>
internal sealed record FileStamp(
    string Path,
    bool Exists,
    long LastWriteTimeUtcTicks,
    long Length,
    string? Sha256,
    bool Promoted);

/// <summary>
///     One cached project (keyed by name; a multi-target-framework project has one entry but several
///     <see cref="CodebaseFragment" />s): its structural inputs, its source-document stamps, and the two
///     invalidation keys.
/// </summary>
/// <param name="ProjectName">The project (assembly) name — the key that ties this entry to its fragments.</param>
/// <param name="CsprojPath">The absolute path to the project file (also present in the structural set).</param>
/// <param name="ProjectDirectory">The project directory, scanned for newly-added <c>*.cs</c> at validation.</param>
/// <param name="ProjectReferences">The names of the projects this one references — the Merkle dependency edges.</param>
/// <param name="Documents">The stamps of the project's source documents (mtime/length and SHA-256).</param>
/// <param name="ContentKey">
///     A hash over this project's own inputs — its document hashes plus its structural (csproj/assets)
///     hashes. Changes iff the project's own content changes.
/// </param>
/// <param name="MerkleKey">
///     A hash over this project's <see cref="ContentKey" /> and its dependencies' <see cref="MerkleKey" />s
///     in a deterministic order, so a change anywhere in the dependency cone changes this key. The dirty set
///     is exactly the projects whose recomputed <see cref="MerkleKey" /> no longer matches.
/// </param>
// ContentKey is persisted schema: validation recomputes keys bottom-up from documents rather than reading
// the stored value, which exists so a manifest diff shows whether a project's own content or only its
// dependency cone moved.
// ReSharper disable NotAccessedPositionalProperty.Global
internal sealed record ProjectCacheEntry(
    string ProjectName,
    string CsprojPath,
    string ProjectDirectory,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<FileStamp> Documents,
    string ContentKey,
    string MerkleKey);

// ReSharper restore NotAccessedPositionalProperty.Global

/// <summary>
///     A recorded spec resolution: the normalized <c>--spec</c> argument that produced it mapped to the
///     project to exclude and the Debug-evaluated output path. Stored faithfully so a cache hit can replay
///     spec resolution without a workspace (Phase 11 WP7 consumes these — it re-runs the built-output check
///     over <see cref="OutputFilePath" /> for a convention/csproj spec, so the hit path resolves identically
///     to a cold run, including the sibling-configuration fallback and its error text).
/// </summary>
/// <param name="NormalizedSpecArgument">
///     The normalized <c>--spec</c> value this record resolves (an absolute csproj/dll path, or the empty
///     string for the no-<c>--spec</c> convention default).
/// </param>
/// <param name="ExcludeProjectName">The solution-member project to drop from the checked universe, or null.</param>
/// <param name="OutputFilePath">The Debug-evaluated output path of the spec project, or null for an explicit DLL.</param>
internal sealed record SpecResolutionRecord(
    string NormalizedSpecArgument,
    string? ExcludeProjectName,
    string? OutputFilePath);