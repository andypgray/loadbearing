namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The persisted extraction cache as a single serializable document: a self-describing manifest plus
///     every <see cref="CodebaseFragment" /> the last workspace-loaded run extracted. One
///     <see cref="ExtractionCacheStore" /> reads, validates, and (re)writes exactly one of these to one
///     atomic file. The manifest is deliberately self-contained — it records every input
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
///         loud tamper), this cache is disposable local derived data: a garbled, truncated, or hand-edited
///         file degrades to a miss rather than an error — see <see cref="ExtractionCacheStore" />.
///     </para>
/// </remarks>
/// <param name="Diagnostics">
///     The workspace-load diagnostics the recorded run collected, replayed verbatim on a hit so cached and
///     cold output are byte-identical on a diagnostic-bearing solution.
/// </param>
/// <param name="UncheckedProjects">
///     The absolute <c>.csproj</c> paths the recorded run did not check — non-empty only when it went
///     through a solution filter. Persisted for the same reason as <see cref="FailedProjects" /> and no
///     other: a hit owns no workspace to recompute it from, and a filtered run whose cached verdict came
///     back as a bare green would be exactly the silence this field exists to break.
/// </param>
/// <param name="FailedProjects">
///     The absolute <c>.csproj</c> paths of the projects that failed to load on the recorded run — half the
///     fail-closed gate's input. Persisted rather than recomputed because a hit owns no workspace to read the
///     loaded structure from, and a hit that answered green where the cold run refuses would be the one thing
///     this cache promises it cannot do.
/// </param>
/// <param name="RestoreFailedProjects">
///     The absolute <c>.csproj</c> paths of the projects whose NuGet packages were not in the model on the
///     recorded run — the gate's other input, persisted for exactly the reason above. It could in principle
///     be recomputed on a hit, since the assets files (and the project files behind the ones with no assets
///     file at all) are still on disk, but it must not be: a hit means every one of those paths stamped
///     identical, so recomputing can only ever agree — at the cost of a parse per project on the path whose
///     whole promise is that it opens nothing.
/// </param>
internal sealed record CacheManifest(
    int SchemaVersion,
    string ToolVersion,
    IReadOnlyList<FileStamp> StructuralStamps,
    IReadOnlyList<ProjectCacheEntry> Projects,
    IReadOnlyList<SpecResolutionRecord> SpecResolutions,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> FailedProjects,
    IReadOnlyList<string> UncheckedProjects,
    IReadOnlyList<string> RestoreFailedProjects,
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
///     One cached project (keyed by name; a multi-target-framework project has one entry — its several
///     <see cref="Microsoft.CodeAnalysis.Project" />s share the name the load boundary normalized them to —
///     but one <see cref="CodebaseFragment" /> per framework): its structural inputs, its source-document
///     stamps, and the two invalidation keys.
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
internal sealed record ProjectCacheEntry(
    string ProjectName,
    string CsprojPath,
    string ProjectDirectory,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<FileStamp> Documents,
    string ContentKey,
    string MerkleKey);

/// <summary>
///     A recorded spec resolution: the normalized <c>--spec</c> argument that produced it mapped to the spec
///     project, the projects to drop from the checked universe, the Debug-evaluated output paths, and the
///     project's intermediate assembly path. Stored faithfully so a cache hit can replay spec resolution
///     without a workspace, resolving identically to a cold run — including the built-output search, what
///     that search refuses, and its error text.
/// </summary>
/// <param name="NormalizedSpecArgument">
///     The normalized <c>--spec</c> value this record resolves (an absolute csproj/dll path, or the empty
///     string for the no-<c>--spec</c> convention default).
/// </param>
/// <param name="SpecProjectName">The solution-member spec project's name, or null for an explicit DLL.</param>
/// <param name="ExcludeProjectNames">
///     Every project to drop from the checked universe — the spec project plus the private plumbing only it
///     references (<c>SpecExclusion</c>). Recorded whole rather than recomputed, because deriving it needs
///     the workspace a hit deliberately never opens.
/// </param>
/// <param name="OutputFilePaths">
///     Every Debug-evaluated output path of the spec project, ordinal-sorted, or empty for an explicit DLL. A
///     multi-target-framework spec project evaluates one per framework, and recording only one would let a hit
///     load a different framework's DLL than the cold run chose — the one thing this record promises it cannot
///     do.
/// </param>
/// <param name="IntermediateAssemblyPath">
///     The spec project's intermediate (<c>obj</c>-side) assembly path, or null when the workspace did not
///     carry one. Recorded because the built-output search reads it to refuse an intermediate result: without
///     it a hit would answer where a cold run refuses. One path is enough for a multi-target-framework
///     project — the search derives the intermediate root from the prefix it shares with the evaluated path,
///     and that root is the same whichever framework's pair it starts from.
/// </param>
internal sealed record SpecResolutionRecord(
    string NormalizedSpecArgument,
    string? SpecProjectName,
    IReadOnlyList<string> ExcludeProjectNames,
    IReadOnlyList<string> OutputFilePaths,
    string? IntermediateAssemblyPath);
