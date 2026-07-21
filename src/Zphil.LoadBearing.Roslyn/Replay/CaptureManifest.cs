using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn.Replay;

/// <summary>
///     The persisted build capture as a single serializable document: a self-describing manifest recording
///     exactly what makes the copied binlog (<c>capture.binlog</c>) a faithful stand-in for a design-time
///     build of the current tree. <see cref="BinlogCaptureStore" /> writes one of these
///     atomically beside the binlog copy and later validates it with zero MSBuild — stat plus a selective
///     re-hash of the recorded structural inputs, and an existence sweep over the recorded documents.
/// </summary>
/// <remarks>
///     <para>
///         <b>Structure-only, deliberately.</b> Unlike the fragment cache (<see cref="CacheManifest" />),
///         which keys on structure <em>and</em> per-document content so any source edit re-extracts, the
///         capture keys on structure alone: replay reads source text from current disk, so a content edit is
///         invisible to it and must not invalidate the capture. What <em>does</em> invalidate it is anything
///         that changes the captured csc command lines' meaning — a csproj/sln/props/targets/global.json/assets
///         edit (caught by the structural stamps) or a change to the source-file membership (caught by the
///         document existence sweep and the project-cone scan).
///     </para>
///     <para>
///         <b>No tamper story.</b> Like the fragment cache, this is disposable local derived data: a garbled,
///         truncated, hand-edited, or version-mismatched manifest degrades to a validation
///         <em>Invalid</em>/<em>Absent</em> and the run falls back to a design-time build — never an exception,
///         never a wrong answer. See <see cref="BinlogCaptureStore" />.
///     </para>
/// </remarks>
/// <param name="SchemaVersion">The manifest schema version; a mismatch is treated as unreadable.</param>
/// <param name="ToolVersion">
///     The writing tool's informational version (per-commit during development); a mismatch invalidates the
///     capture, because replay fidelity is only guaranteed against the version that captured it.
/// </param>
/// <param name="StructuralStamps">
///     Full <see cref="FileStamp" />s (with SHA-256) of the solution file, every C# project's csproj, each
///     project's <c>obj/project.assets.json</c>, and the <c>Directory.Build.props</c>/<c>.targets</c>/
///     <c>global.json</c> probe chains from each project directory up to the solution directory, absence
///     recorded — the exact set <c>ExtractionCacheStore</c> fingerprints, so a structural edit invalidates.
/// </param>
/// <param name="Projects">One entry per C# project in the replayed solution.</param>
/// <param name="BinlogCopyStamp">
///     A stamp of the <c>capture.binlog</c> copy itself; existence-checked at validation (a missing copy is
///     unreadable). Recorded for completeness — the copy's bytes are never re-hashed against it.
/// </param>
internal sealed record CaptureManifest(
    int SchemaVersion,
    string ToolVersion,
    IReadOnlyList<FileStamp> StructuralStamps,
    IReadOnlyList<CaptureProjectEntry> Projects,
    FileStamp BinlogCopyStamp);

/// <summary>
///     One replayed project's identity and its full csc source-file list, as the capture records it.
/// </summary>
/// <param name="ProjectName">The project (assembly) name.</param>
/// <param name="CsprojPath">The absolute path to the project file (also present in the structural set).</param>
/// <param name="ProjectDirectory">The project directory, cone-scanned for newly-added <c>*.cs</c> at validation.</param>
/// <param name="DocumentPaths">
///     The full document-path list from the replayed solution, existence-checked (never hashed) at validation.
///     Deliberately unlike <c>SolutionCacheInputs</c>, this <em>includes</em> obj-generated sources
///     (<c>*.GlobalUsings.g.cs</c>, <c>*.AssemblyInfo.cs</c>, and the like): they are csc inputs the binlog's
///     command line fixes, and replay cannot regenerate them, so a <c>dotnet clean</c> that deletes them must
///     invalidate the capture rather than let replay silently produce a model drifted from the real build.
/// </param>
/// <param name="ConeFiles">
///     The project cone's <c>*.cs</c> (bin/obj excluded) as it stood at ingest — the membership the cone scan
///     compares against. Recorded because the cone is a superset of <see cref="DocumentPaths" />: a
///     <c>&lt;Compile Remove&gt;</c>'d or <c>None</c>-typed <c>*.cs</c> is in the cone but not compiled, so
///     without this snapshot the scan would read it as a perpetual add and invalidate the capture on every
///     run. A file the scan finds that is in neither <see cref="ConeFiles" /> nor <see cref="DocumentPaths" />
///     is a genuine post-ingest add.
/// </param>
// ProjectName is persisted schema: the self-describing manifest records each project's identity for a
// readable manifest diff, though validation re-collects projects from the solution and keys on the
// directory, csproj, and document set rather than reading the stored name back.
// ReSharper disable NotAccessedPositionalProperty.Global
internal sealed record CaptureProjectEntry(
    string ProjectName,
    string CsprojPath,
    string ProjectDirectory,
    IReadOnlyList<string> DocumentPaths,
    IReadOnlyList<string> ConeFiles);

// ReSharper restore NotAccessedPositionalProperty.Global