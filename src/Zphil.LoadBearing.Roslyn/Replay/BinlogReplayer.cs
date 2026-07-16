using Basic.CompilerLog.Util;
using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn.Replay;

/// <summary>
///     One-shot binlog replay for the enforcement path (Phase 12 D1): read a real build's binlog into a
///     workspace-shaped <see cref="Solution" /> without a design-time build, strip unresolved references, and
///     return. The replayed solution is the design-time-build bypass — it carries the structure the
///     build recorded (project files, output paths, references, the csc source-file list) while the
///     document text is read from <em>current disk state</em> at materialisation time, so edits made
///     after the capture appear in the model and the binlog only goes stale on a structural change.
/// </summary>
/// <remarks>
///     <para>
///         This path bypasses the <em>design-time build</em>: it uses <c>Basic.CompilerLog.Util</c> plus an
///         <see cref="AdhocWorkspace" />, so — unlike <see cref="WorkspaceLoader" /> — it never opens an
///         <c>MSBuildWorkspace</c> and never spawns an out-of-process build host. It is <em>not</em> free of
///         MSBuild assemblies, though: the reader's binlog parser (MSBuild.StructuredLogger, pulled in by
///         <c>Basic.CompilerLog.Util</c>) binds <c>Microsoft.Build.Framework</c> at runtime, resolved through
///         the host's <c>MSBuildLocator</c> registration. That is a load-bearing constraint, not an
///         implementation detail to simplify away:
///         <b>
///             a caller must register MSBuildLocator before
///             replaying
///         </b>
///         (the CLI's <c>MsBuildGate</c> does, once up front), or the parser fails to load
///         <c>Microsoft.Build.Framework</c> at runtime.
///     </para>
///     <para>
///         Replaying analyzers/generators executes the build's own analyzer assemblies from their
///         on-disk paths (see <see cref="Replay" /> for the analyzer-host choice) — the same trust
///         boundary as building the solution in the first place.
///     </para>
/// </remarks>
internal static class BinlogReplayer
{
    /// <summary>
    ///     Replays <paramref name="binlogPath" /> into a <see cref="ReplayedSolution" />: opens the
    ///     binlog, keeps only the regular C# compiler invocations, builds a <see cref="SolutionInfo" />,
    ///     loads it into a fresh <see cref="AdhocWorkspace" />, and strips unresolved references exactly
    ///     as <see cref="WorkspaceLoader" /> does so downstream behaviour is identical.
    /// </summary>
    /// <param name="binlogPath">Absolute path to the <c>.binlog</c> produced by a real build of the current tree.</param>
    /// <param name="diagnosticSink">
    ///     Optional sink for replay diagnostics that do not abort the load: workspace-load failures and a
    ///     note when unresolved references had to be stripped (a build artifact missing from disk). Mirrors
    ///     <see cref="WorkspaceLoader" />'s <c>diagnosticLog</c> contract.
    /// </param>
    /// <param name="ct">Cancellation token; honoured on a best-effort basis around the synchronous reader calls.</param>
    /// <returns>The replayed, stripped solution with its owning workspace and reader.</returns>
    /// <remarks>
    ///     Synchronous, because the underlying <see cref="SolutionReader" /> API is synchronous.
    ///     Exception surface (the CLI wraps these in a later work package; this method does not reference
    ///     that mapping):
    ///     <list type="bullet">
    ///         <item><see cref="ArgumentException" /> — <paramref name="binlogPath" /> is null or blank.</item>
    ///         <item><see cref="FileNotFoundException" /> — no file exists at <paramref name="binlogPath" />.</item>
    ///         <item>
    ///             <see cref="InvalidOperationException" /> — the binlog holds no replayable C# compiler
    ///             invocation (a VB-only or empty capture cannot produce a model).
    ///         </item>
    ///         <item>
    ///             Any exception the underlying reader raises for an unreadable, corrupt, or non-binlog
    ///             file (for example an <see cref="ArgumentException" /> for an unrecognised extension)
    ///             propagates unwrapped.
    ///         </item>
    ///     </list>
    ///     The analyzer host is <see cref="BasicAnalyzerKind.OnDisk" />: it executes the build's own
    ///     analyzers/generators, surfaced as on-disk <c>AnalyzerFileReference</c> instances — the shape the
    ///     <c>MSBuildWorkspace</c> path itself produces (it surfaces analyzers/generators as on-disk file
    ///     references and runs them when producing compilations), which is why it is the fidelity match.
    ///     <see cref="BasicAnalyzerKind.None" /> is rejected deliberately: it would skip generator execution
    ///     and inject the capture's <em>stale</em> generated files, breaking the read-from-current-disk
    ///     contract; <see cref="BasicAnalyzerKind.InMemory" /> also runs generators but loads analyzers into
    ///     a private context rather than the file-reference shape MSBuild uses.
    ///     The differential fidelity test is the gate on this choice.
    /// </remarks>
    public static ReplayedSolution Replay(
        string binlogPath, Action<string>? diagnosticSink = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binlogPath);
        if (!File.Exists(binlogPath))
            throw new FileNotFoundException($"Binlog not found for replay: '{binlogPath}'.", binlogPath);

        ct.ThrowIfCancellationRequested();

        var reader = SolutionReader.Create(
            binlogPath, BasicAnalyzerKind.OnDisk, predicate: IsReplayableCSharpCall);

        AdhocWorkspace? workspace = null;
        try
        {
            if (reader.ProjectCount == 0)
                throw new InvalidOperationException(
                    $"The binlog '{binlogPath}' contains no C# compiler invocations to replay. LoadBearing "
                    + "replays C# projects only; a Visual Basic-only or empty binlog cannot produce a model.");

            ct.ThrowIfCancellationRequested();
            SolutionInfo solutionInfo = reader.ReadSolutionInfo();

            workspace = new AdhocWorkspace();
            workspace.RegisterWorkspaceFailedHandler(e =>
            {
                if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) diagnosticSink?.Invoke(e.Diagnostic.Message);
            });

            Solution solution = workspace.AddSolution(solutionInfo);
            solution = NormalizeProjects(solution);
            (Solution stripped, int analyzerCount, int metadataCount) = solution.StripUnresolvedReferences();
            if (analyzerCount > 0 || metadataCount > 0)
                diagnosticSink?.Invoke(
                    $"Replay stripped {analyzerCount} unresolved analyzer and {metadataCount} unresolved "
                    + "metadata reference(s); a build artifact recorded in the binlog was missing from disk.");

            return new ReplayedSolution(workspace, reader, stripped);
        }
        catch
        {
            workspace?.Dispose();
            reader.Dispose();
            throw;
        }
    }

    // Two SolutionReader project-metadata spellings need aligning with what MSBuildWorkspace produces, so
    // the extractor, the persisted cache, SpecResolver, and the goldens all see identical data on either path.
    private static Solution NormalizeProjects(Solution solution)
    {
        foreach (ProjectId projectId in solution.ProjectIds.ToList())
        {
            Project project = solution.GetProject(projectId)!;

            // (1) Name: SolutionReader names each project after its csproj file *with* the extension
            //     (CompilerCall.ProjectFileName, e.g. "MyApp.Domain.csproj"); MSBuildWorkspace names it
            //     without ("MyApp.Domain"). Everything downstream keys on the extension-less spelling — the
            //     extractor's ProjectName and project-reference resolution, the cache's project keys, and
            //     SpecResolver's exclude set — so strip it for byte-parity with the cold path.
            string trimmedName = Path.GetFileNameWithoutExtension(project.Name);
            if (!string.Equals(trimmedName, project.Name, StringComparison.Ordinal))
                solution = solution.WithProjectName(projectId, trimmedName);

            // (2) OutputFilePath: SolutionReader builds it from the assembly *name* with no extension
            //     (<obj-dir>/<AssemblyName>), so it names a file that is not on disk. Append the extension
            //     the OutputKind implies to point at the real obj-intermediate assembly — the form
            //     SpecResolver.RequireBuiltOutput and the cache's SpecResolutionRecord consume. The obj path
            //     (vs MSBuild's bin path) is deliberately kept: nothing compares its bytes, only resolves it.
            if (project.OutputFilePath is { } outputFilePath)
            {
                string withExtension = EnsureAssemblyExtension(outputFilePath, project.CompilationOptions?.OutputKind);
                if (!string.Equals(withExtension, outputFilePath, StringComparison.Ordinal))
                    solution = solution.WithProjectOutputFilePath(projectId, withExtension);
            }
        }

        return solution;
    }

    // Appends the assembly file extension the OutputKind implies when the path carries none. Path.GetExtension
    // is unusable here — a dotted assembly name ("MyApp.Legacy.Billing") reads as extension ".Billing" — so
    // the guard tests the known assembly extensions explicitly.
    private static string EnsureAssemblyExtension(string outputPath, OutputKind? outputKind)
    {
        string[] assemblyExtensions = [".dll", ".exe", ".netmodule", ".winmdobj"];
        if (assemblyExtensions.Any(ext => outputPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return outputPath;

        string extension = outputKind switch
        {
            OutputKind.ConsoleApplication or OutputKind.WindowsApplication or OutputKind.WindowsRuntimeApplication => ".exe",
            OutputKind.NetModule => ".netmodule",
            OutputKind.WindowsRuntimeMetadata => ".winmdobj",
            _ => ".dll"
        };
        return outputPath + extension;
    }

    // The C#-only filter, layered onto SolutionReader's own default (regular compiler calls). VB projects
    // surface as CompilerCalls with IsCSharp == false; dropping them here is how the C#-only contract is
    // enforced, so a mixed-language solution replays its C# projects and silently ignores the rest.
    private static bool IsReplayableCSharpCall(CompilerCall call)
    {
        return call.Kind == CompilerCallKind.Regular && call.IsCSharp;
    }
}