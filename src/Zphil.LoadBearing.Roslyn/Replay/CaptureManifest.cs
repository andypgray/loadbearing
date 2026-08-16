using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Roslyn.Replay;

/// <summary>
///     The persisted build capture as a single serializable document: a self-describing manifest recording
///     exactly what makes the copied binlog (<c>capture.binlog</c>) a faithful stand-in for a design-time
///     build of the current tree. <see cref="BinlogCaptureStore" /> writes one of these atomically beside
///     the binlog copy.
/// </summary>
/// <remarks>
///     Why the capture keys on structure alone, and why a manifest it cannot read degrades rather than
///     throws, belong to <see cref="BinlogCaptureStore" /> — the type that writes these and enforces both.
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
/// <param name="EvaluatedOutputPath">
///     The project's evaluated output path as the replayed solution carried it, or null. Paired with
///     <paramref name="IntermediateAssemblyPath" /> it is what locates the restore assets file under a
///     non-default output layout (see <see cref="IntermediateOutputTree" />). A replayed solution often
///     carries neither, and then the default location is all the capture stamps — the same set it stamped
///     before this pair existed.
/// </param>
/// <param name="IntermediateAssemblyPath">The project's intermediate assembly path as replayed, or null.</param>
// ProjectName is persisted schema: the self-describing manifest records each project's identity for a
// readable manifest diff, though validation re-collects projects from the solution and keys on the
// directory, csproj, and document set rather than reading the stored name back. The two evaluated paths
// are recorded for the same reason — they decide which structural paths the capture stamped, so a manifest
// diff that shows the stamps should show what produced them.
internal sealed record CaptureProjectEntry(
    string ProjectName,
    string CsprojPath,
    string ProjectDirectory,
    IReadOnlyList<string> DocumentPaths,
    IReadOnlyList<string> ConeFiles,
    string? EvaluatedOutputPath = null,
    string? IntermediateAssemblyPath = null);
