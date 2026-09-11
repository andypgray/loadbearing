using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Cli.SpecLoading;

/// <summary>
///     One candidate spec project reduced to the tuple the convention needs — so the core resolves without a
///     workspace.
/// </summary>
/// <remarks>
///     <see cref="ReferencePaths" /> carries the project's PE metadata reference paths <em>plus</em>
///     the output paths of its direct project references: a spec project references the contract library as a
///     package (PE metadata) once published, but as a <c>ProjectReference</c> in a source checkout, and the
///     convention must see both.
///     <see cref="FilePath" /> is the candidate's own <c>.csproj</c>, and it is what the convention counts by:
///     a multi-target-framework project file yields one Roslyn project per framework, and those are one
///     candidate rather than an ambiguity. It carries no default on purpose — a caller with no project file in
///     hand has to say so out loud, because a silent default would fold every unknown-path candidate together
///     and turn genuine ambiguity green.
///     <see cref="IsDeclaredMember" /> says whether the solution file declares this project, so the convention
///     considers only solution material: a rule-pack library that a spec project drags into the workspace also
///     references the contract library, and would otherwise turn every composing solution into "Multiple spec
///     projects found".
///     <see cref="IntermediateAssemblyPath" /> is the project's <c>obj</c>-side assembly, which the
///     built-output search uses to refuse an intermediate result. It reads better beside
///     <see cref="OutputFilePath" />, and is last anyway: inserting it there would rebind the positional
///     <see cref="IsDeclaredMember" /> argument every construction already passes to a <c>string?</c>.
/// </remarks>
internal sealed record SpecProjectCandidate(
    string Name,
    IReadOnlyList<string> ReferencePaths,
    string? OutputFilePath,
    string? FilePath,
    bool IsDeclaredMember = true,
    string? IntermediateAssemblyPath = null);

/// <summary>
///     The resolved spec: the DLL to load and, when the spec is a solution member, its project name plus the
///     projects to exclude from the checked universe — the spec project and its private plumbing
///     (<see cref="SpecExclusion" />). Both are empty/null for a prebuilt DLL, which excludes nothing.
/// </summary>
/// <param name="DllPath">The built spec assembly to load.</param>
/// <param name="SpecProjectName">The spec project's name, or null for a prebuilt DLL.</param>
/// <param name="ExcludeProjectNames">The projects the checked universe drops, empty for a prebuilt DLL.</param>
/// <param name="OutputFilePaths">
///     The evaluated output paths <see cref="DllPath" /> was chosen from — every framework's, ordinal-sorted
///     — or null for a prebuilt DLL, which was never searched for. Carried because the extraction cache
///     records what the resolution consumed: re-deriving it can only group the spec project's Roslyn projects
///     by name, where resolution grouped them by canonicalized project file.
/// </param>
/// <param name="IntermediateAssemblyPath">
///     The <c>obj</c>-side assembly path the built-output search refused an intermediate result under, or
///     null for a prebuilt DLL. Recorded for the same reason as <see cref="OutputFilePaths" />: without it a
///     cache hit would answer with the intermediate a cold run refuses.
/// </param>
internal sealed record SpecResolution(
    string DllPath,
    string? SpecProjectName,
    IReadOnlyCollection<string> ExcludeProjectNames,
    IReadOnlyList<string>? OutputFilePaths = null,
    string? IntermediateAssemblyPath = null);

/// <summary>
///     Resolves which spec DLL to load. The CLI
///     never builds: <c>--spec</c> takes a prebuilt DLL or a solution-member csproj (resolved to its
///     output DLL); with no <c>--spec</c>, the convention picks the unique <em>declared</em> solution project
///     that references <c>Zphil.LoadBearing.dll</c>. Every failure is a loud <see cref="UserErrorException" />;
///     a spec project that is a solution member is excluded from the codebase the checker sees, along with the
///     plumbing only it pulls in.
/// </summary>
internal static class SpecResolver
{
    private const string CoreAssemblyFile = "Zphil.LoadBearing.dll";

    // How many entries a refusal quotes before it counts the rest. A refusal nobody reads to the end names
    // nothing; three is enough to see whether the failures share a cause.
    private const int MaxQuotedDiagnostics = 3;

    /// <summary>
    ///     Resolves the spec against a loaded solution.
    /// </summary>
    /// <param name="solution">The loaded solution.</param>
    /// <param name="declaredMembers">
    ///     The solution's declared <c>.csproj</c> members, or null when membership could not be read. Passed
    ///     in rather than read here because both branches need it and the caller needs the same set again for
    ///     extraction — one read serves all three.
    /// </param>
    /// <param name="specArgument">The <c>--spec</c> value, or null/blank for the convention default.</param>
    /// <param name="diagnostics">
    ///     How well the workspace loaded. Not defaulted, and deliberately: an unresolved package reference
    ///     makes the convention find nothing, so "the workspace loaded cleanly" is what decides whether zero
    ///     candidates means "there is no spec project" or "the evidence for one did not resolve" — and unlike
    ///     an intermediate assembly path, that is never genuinely unknown to a caller. A default is also
    ///     untypeable: <see cref="WorkspaceDiagnostics.None" /> is a property, and
    ///     <c>default(WorkspaceDiagnostics)</c> carries null lists.
    /// </param>
    internal static SpecResolution Resolve(
        Solution solution, IReadOnlySet<string>? declaredMembers, string? specArgument,
        WorkspaceDiagnostics diagnostics)
    {
        return string.IsNullOrWhiteSpace(specArgument)
            ? ResolveByConvention(solution, declaredMembers, diagnostics)
            : ResolveExplicit(solution, declaredMembers, specArgument);
    }

    /// <summary>
    ///     The workspace-free half of resolution: a built-DLL <c>--spec</c> resolves directly, because that
    ///     branch never touches the <see cref="Solution" />.
    /// </summary>
    /// <returns>
    ///     The resolution, or <see langword="null" /> when resolution needs the workspace — the convention
    ///     default (no <c>--spec</c>) and a solution-member csproj.
    /// </returns>
    /// <remarks>
    ///     A DLL path that does not exist is still a loud error. This is what lets <c>explain</c> run with no
    ///     MSBuild load at all.
    /// </remarks>
    internal static SpecResolution? TryResolveWithoutSolution(string? specArgument)
    {
        if (string.IsNullOrWhiteSpace(specArgument)) return null;
        if (specArgument.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return null;

        string fullPath = Path.GetFullPath(specArgument);
        if (!File.Exists(fullPath))
            throw new UserErrorException($"--spec '{specArgument}' was not found. Pass a built spec DLL or a solution-member csproj.");

        return new SpecResolution(fullPath, null, []);
    }

    private static SpecResolution ResolveExplicit(
        Solution solution, IReadOnlySet<string>? declaredMembers, string specArgument)
    {
        if (specArgument.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            string canonicalPath = PathCanonicalizer.Resolve(Path.GetFullPath(specArgument));
            List<CanonicalProject> projects = CanonicalProjectsOf(solution);
            List<CanonicalProject> sharingProjects = ProjectsSharingFile(projects, canonicalPath);
            if (sharingProjects.Count == 0)
                throw new UserErrorException(
                    $"--spec '{specArgument}' is not a project in the solution. " +
                    "Pass a built spec DLL or a csproj that is a member of the target solution.");

            BuiltOutputInputs built = BuiltOutputsOf(sharingProjects);
            return MemberResolution(solution, projects, declaredMembers, sharingProjects[0].Project.Name, built);
        }

        // A DLL path — the branch that needs no solution.
        return TryResolveWithoutSolution(specArgument)!;
    }

    private static SpecResolution ResolveByConvention(
        Solution solution, IReadOnlySet<string>? declaredMembers, WorkspaceDiagnostics diagnostics)
    {
        List<CanonicalProject> projects = CanonicalProjectsOf(solution);

        List<SpecProjectCandidate> candidates = projects
            .Where(entry => entry.Project.Language == LanguageNames.CSharp)
            .Select(entry => new SpecProjectCandidate(
                entry.Project.Name,
                ReferencePathsOf(entry.Project, solution),
                entry.Project.OutputFilePath,
                entry.Project.FilePath,
                SpecExclusion.IsDeclaredMember(declaredMembers, entry.Project.FilePath, entry.CanonicalFilePath),
                entry.Project.CompilationOutputInfo.AssemblyPath))
            .ToList();

        SpecProjectCandidate chosen = ResolveConventionProject(candidates, diagnostics);
        BuiltOutputInputs built = BuiltOutputsOfProjectFile(
            projects, chosen.FilePath, chosen.OutputFilePath, chosen.IntermediateAssemblyPath);
        return MemberResolution(solution, projects, declaredMembers, chosen.Name, built);
    }

    // The shared tail of both solution-member branches: the built DLL plus the projects the checked universe
    // drops — the spec project and the plumbing only it references. A null declaredMembers is the unreadable
    // case, which SpecExclusion degrades to just the spec project. The evaluated paths are normalized once
    // here — the search's own filter-and-sort, which is idempotent — because they both feed the search and
    // ride on to the extraction cache, which has to record the set the search actually consumed.
    private static SpecResolution MemberResolution(
        Solution solution,
        IReadOnlyList<CanonicalProject> projects,
        IReadOnlySet<string>? declaredMembers,
        string specProjectName,
        BuiltOutputInputs built)
    {
        List<string> evaluatedPaths = NormalizeEvaluatedPaths(built.OutputFilePaths);

        return new SpecResolution(
            RequireBuiltOutput(specProjectName, evaluatedPaths, built.IntermediateAssemblyPath),
            specProjectName,
            SpecExclusion.Compute(ExclusionProjectsOf(solution, projects), declaredMembers, specProjectName),
            evaluatedPaths,
            built.IntermediateAssemblyPath);
    }

    // The exclusion walk's view of the solution, projected off this resolution's own canonical view rather
    // than re-derived from the workspace: the walk's membership test canonicalizes each project file, and
    // every one of those paths is already resolved here. Calling the pure core directly is what carries the
    // canonical spelling in — the workspace-taking overload has no view to carry.
    private static List<SpecExclusionProject> ExclusionProjectsOf(
        Solution solution, IReadOnlyList<CanonicalProject> projects)
    {
        return projects
            .Select(entry => new SpecExclusionProject(
                entry.Project.Name,
                entry.Project.FilePath,
                ReferencedProjectNames(entry.Project, solution),
                entry.CanonicalFilePath))
            .ToList();
    }

    private static IReadOnlyList<string> ReferencedProjectNames(Project project, Solution solution)
    {
        return project.ProjectReferences
            .Select(reference => solution.GetProject(reference.ProjectId)?.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();
    }

    // Both built-output facts about one project file, from one pass over the solution.
    //
    // A multi-target-framework csproj yields one Roslyn project per framework, so any single project's
    // OutputFilePath names an arbitrary framework's DLL; the built-output check needs them all so it can pick
    // one that is actually on disk — and so a cache hit, which records the same set, replays the identical
    // choice. One intermediate path suffices even then: the search derives the intermediate ROOT by peeling
    // the prefix the evaluated and intermediate paths share, and the two diverge at the same segment level
    // whichever framework's pair it starts from, so the derived root is framework-invariant. Ordinal-first
    // keeps that choice deterministic rather than load-order dependent.
    //
    // Both sides of the scan are canonicalized exactly once: canonicalizing probes the filesystem for a
    // reparse point per path segment, so re-resolving the needle per candidate — or the haystack per scan —
    // costs one such walk per project in the solution, on every warm tool call.
    private static BuiltOutputInputs BuiltOutputsOfProjectFile(
        IReadOnlyList<CanonicalProject> projects,
        string? projectFilePath,
        string? evaluatedOutputFilePath,
        string? evaluatedIntermediatePath)
    {
        if (string.IsNullOrEmpty(projectFilePath))
            return new BuiltOutputInputs([evaluatedOutputFilePath], evaluatedIntermediatePath);

        string canonicalPath = PathCanonicalizer.Resolve(projectFilePath);
        List<CanonicalProject> sharingProjects = ProjectsSharingFile(projects, canonicalPath);
        return BuiltOutputsOf(sharingProjects);
    }

    // The two built-output facts, read off the projects one .csproj was built into.
    private static BuiltOutputInputs BuiltOutputsOf(IReadOnlyList<CanonicalProject> sharingProjects)
    {
        List<string?> outputFilePaths = sharingProjects
            .Select(entry => entry.Project.OutputFilePath)
            .ToList();
        string? intermediateAssemblyPath = sharingProjects
            .Select(entry => entry.Project.CompilationOutputInfo.AssemblyPath)
            .Where(path => !string.IsNullOrEmpty(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        return new BuiltOutputInputs(outputFilePaths, intermediateAssemblyPath);
    }

    // Every project the solution carries, beside the canonicalized spelling of its own .csproj, resolved
    // once for the whole resolution: both scans a resolution runs — which project a --spec needle names, and
    // which projects a chosen .csproj was built into — ask their question against this one view.
    private static List<CanonicalProject> CanonicalProjectsOf(Solution solution)
    {
        return solution.Projects
            .Select(project => new CanonicalProject(project, CanonicalFileOf(project)))
            .ToList();
    }

    private static string? CanonicalFileOf(Project project)
    {
        return string.IsNullOrEmpty(project.FilePath) ? null : PathCanonicalizer.Resolve(project.FilePath);
    }

    // The projects built from one already-canonicalized .csproj, in solution order — a multi-target-framework
    // project file yields one Roslyn project per framework. Canonicalizing this side too (symlinks resolved)
    // is what lets a solution opened through a symlinked root still match, and the per-OS comparison
    // (PathComparison) is what stops the match from missing across symlink spellings or over-matching
    // case-variant paths on a case-sensitive file system.
    private static List<CanonicalProject> ProjectsSharingFile(
        IReadOnlyList<CanonicalProject> projects, string canonicalProjectFile)
    {
        return projects
            .Where(entry => entry.CanonicalFilePath is { } canonical
                            && string.Equals(canonical, canonicalProjectFile, PathComparison.Comparison))
            .ToList();
    }

    /// <summary>
    ///     A project's reference paths as the convention sees them: PE metadata references (the package /
    ///     built-DLL shape) plus the output paths of direct project references (the source-checkout shape,
    ///     where the contract library arrives as a <c>ProjectReference</c> and never appears among the PE
    ///     metadata references).
    /// </summary>
    private static IReadOnlyList<string> ReferencePathsOf(Project project, Solution solution)
    {
        IEnumerable<string> metadataPaths = project.MetadataReferences
            .OfType<PortableExecutableReference>()
            .Select(r => r.FilePath ?? string.Empty);

        IEnumerable<string> projectReferenceOutputs = project.ProjectReferences
            .Select(r => solution.GetProject(r.ProjectId)?.OutputFilePath ?? string.Empty);

        return metadataPaths.Concat(projectReferenceOutputs).ToList();
    }

    /// <summary>
    ///     The pure convention core: the unique <em>declared</em> solution member that references
    ///     <c>Zphil.LoadBearing.dll</c>. Zero candidates and multiple candidates are both loud errors
    ///     (unit-tested over tuples). Undeclared projects are workspace passengers — a spec project's own
    ///     <c>ProjectReference</c>s pull the contract library and any rule-pack library in — and a passenger
    ///     that references the contract is not a spec project the user chose to have. Several Roslyn projects
    ///     sharing one <c>.csproj</c> (a multi-target-framework spec project) count once.
    /// </summary>
    /// <remarks>
    ///     Zero candidates is reported against <paramref name="diagnostics" />, because a workspace that did
    ///     not load cleanly produces exactly the same zero: a project that failed to load declares no
    ///     reference to match, and an unresolved package reference means the spec project's reference to the
    ///     contract library is simply not there to match either.
    /// </remarks>
    internal static SpecProjectCandidate ResolveConventionProject(
        IReadOnlyList<SpecProjectCandidate> candidates, WorkspaceDiagnostics diagnostics)
    {
        List<SpecProjectCandidate> matches = candidates.Where(c => c.IsDeclaredMember && ReferencesCore(c)).ToList();

        if (matches.Count == 0) throw NoSpecProjectFound(candidates, diagnostics);

        List<IGrouping<string, SpecProjectCandidate>> groups = matches
            .Select((candidate, index) => (Candidate: candidate, Key: GroupKey(candidate, index)))
            .GroupBy(match => match.Key, match => match.Candidate, StringComparer.Ordinal)
            .ToList();

        if (groups.Count > 1)
        {
            IOrderedEnumerable<string> lines = groups
                .Select(group => DisambiguationLine(group.First()))
                .OrderBy(line => line, StringComparer.Ordinal);

            throw new UserErrorException(
                "Multiple spec projects found; pass --spec to disambiguate:\n  " + string.Join("\n  ", lines));
        }

        return matches[0];
    }

    // Zero candidates has causes whose remedies do not overlap, and reporting only the first sent readers to
    // write an argument that could not help them. Four arms, strongest evidence first. The first two are the
    // incomplete-model gate's own two causes, said in its words (IncompleteModelGate.SpecRefusal) and only
    // reached from here — so they compose rather than exclude, one run being able to break both ways at once:
    //
    //  1. Projects failed to load. The one that would have matched may be among them, and no --spec argument
    //     repairs a load — so this arm names them and points at the build.
    //  2. A project's NuGet packages did not resolve. That is the measured locked-mode shape: a broken
    //     restore leaves the spec project's reference to the contract library unresolved while the project
    //     itself still loads completely, so the convention finds nothing and the reader must repair the
    //     restore rather than name a project.
    //  3. Nothing failed either way, but the load reported problems about this solution — diagnostics with no
    //     project blamed. It says "may", because that is all it knows.
    //  4. A clean load means the solution really has no spec project. The sentence stays byte-identical (the
    //     derive_spec prompt quotes it), plus how many projects were considered, which is what tells a reader
    //     whether the workspace held what they expected.
    //
    // NuGetAudit advisories are excluded from arm 3's input, and only from arm 3's: an advisory's publication
    // date says nothing about whether this solution's references resolved, so a solution that genuinely has
    // no spec project must not be sent to go and fix its restore. Nothing here gates — every arm is the same
    // refusal with different evidence — so no exit code turns on that distinction.
    private static UserErrorException NoSpecProjectFound(
        IReadOnlyList<SpecProjectCandidate> candidates, WorkspaceDiagnostics diagnostics)
    {
        // The refusal is thrown before a CodebaseSource exists, so no runner renders the evidence beside it —
        // the message has to carry it itself, in the shape every other evidence block takes. CliErrorMapper
        // writes it a line apiece, so this reads the same on stderr and in an MCP error result.
        if (diagnostics.IsIncomplete)
            return new UserErrorException(IncompleteModelGate.SpecRefusal(diagnostics));

        if (diagnostics.ActionableDiagnostics.Count > 0)
            return new UserErrorException(
                EvidenceBlock.Compose(
                    "No spec project found: the workspace did not load cleanly, so a project that references "
                    + "Zphil.LoadBearing.dll may have failed to resolve it:",
                    diagnostics.ActionableDiagnostics,
                    "Restore and build the solution first (dotnet restore, dotnet build), then retry.",
                    withSelectionNote: true,
                    quoteCap: MaxQuotedDiagnostics));

        return new UserErrorException(
            "No spec project found: no solution project references Zphil.LoadBearing.dll. Pass --spec to name one.\n"
            + $"Considered {candidates.Count} C# project(s) in the workspace.");
    }

    // One csproj that yields several Roslyn Projects (multi-TFM) is ONE candidate, not many. Group on
    // the canonicalized project file, folded so a plain ordinal comparer carries the per-OS case rule.
    // A candidate with no project file is its own group: folding the unknown-path candidates together
    // would turn genuine ambiguity green, and '\0' cannot occur in a path so the sentinel cannot collide.
    private static string GroupKey(SpecProjectCandidate candidate, int index)
    {
        return string.IsNullOrEmpty(candidate.FilePath)
            ? $"\0{index}"
            : PathComparison.Fold(PathCanonicalizer.Resolve(candidate.FilePath));
    }

    // One line of the ambiguity error. The project file is what the reader has to pass to --spec, so it is
    // named whenever it is known; a candidate with no project file renders as the bare name it always did.
    private static string DisambiguationLine(SpecProjectCandidate candidate)
    {
        return string.IsNullOrEmpty(candidate.FilePath)
            ? candidate.Name
            : $"{candidate.Name}  ({candidate.FilePath})";
    }

    // Over the file name as a span: a large solution carries tens of thousands of metadata reference paths,
    // and the substring GetFileName(string) would allocate for each is one comparison's worth of garbage.
    private static bool ReferencesCore(SpecProjectCandidate candidate)
    {
        return candidate.ReferencePaths.Any(path =>
            Path.GetFileName(path.AsSpan()).Equals(CoreAssemblyFile, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     The one built output the spec loads from, given every path the projects behind one <c>.csproj</c>
    ///     evaluated to — one per target framework for a multi-target-framework spec project. The first in
    ///     ordinal path order that is actually on disk wins, each path getting the
    ///     <see cref="BuiltOutputProbe">built-output search</see> before the next is tried; none built is a
    ///     loud error naming every path tried.
    /// </summary>
    /// <remarks>
    ///     Which framework's DLL wins is deliberately "whichever is built", not "whichever matches the host":
    ///     the CLI never builds, so existence is the question that matters, and a spec DLL is loaded across
    ///     frameworks routinely (this repo loads a <c>net48</c> spec from a <c>net10.0</c> host).
    /// </remarks>
    internal static string RequireBuiltOutput(
        string projectName, IReadOnlyList<string?> outputFilePaths, string? intermediateAssemblyPath = null)
    {
        List<string> evaluatedPaths = NormalizeEvaluatedPaths(outputFilePaths);

        foreach (string outputFilePath in evaluatedPaths)
        {
            // The evaluated path exists: it is the answer, with no search and — deliberately — no
            // intermediate refusal. Binlog replay hands an obj-side assembly as this primary argument
            // (BinlogReplayer.NormalizeProjects), because a capture records no other path, so refusing an
            // intermediate here would refuse every replayed run.
            if (File.Exists(outputFilePath)) return outputFilePath;

            // The evaluated path can name a directory no build ever writes, in any configuration — the
            // shapes are BuiltOutputProbe's remarks — so search the tree the build actually wrote.
            if (BuiltOutputProbe.Find(outputFilePath, intermediateAssemblyPath) is { } built) return built;
        }

        throw new UserErrorException(
            $"The spec project '{projectName}' has no built output" +
            (evaluatedPaths.Count == 0 ? "" : $" at '{string.Join("' or '", evaluatedPaths)}'") +
            ". Build the solution first (dotnet build).");
    }

    /// <summary>
    ///     Replays a recorded resolution: the candidate half is kept exactly as recorded, and only the built
    ///     output is resolved again — live, against disk, through the same
    ///     <see cref="RequireBuiltOutput" /> a cold run uses.
    /// </summary>
    /// <remarks>
    ///     The one owner of that split, because two caches now need it. The persisted extraction cache
    ///     replays a record it read off disk; the warm session replays a resolution it computed under an
    ///     earlier tool call. Both hold a candidate half derived from a workspace they no longer have, and a
    ///     spec output that has since been rebuilt, moved or deleted has to answer in the cold run's words —
    ///     the bounded search from the output root, what that search refuses, and its refusal text. Running
    ///     that half here rather than in each caller is what makes the two agreeing structural instead of a
    ///     thing to remember.
    /// </remarks>
    /// <param name="specProjectName">The recorded spec project's name, or null when the record has none.</param>
    /// <param name="fallbackProjectName">
    ///     What a refusal names when <paramref name="specProjectName" /> is null — the normalized
    ///     <c>--spec</c> argument, which is the only spelling of the spec the reader ever typed.
    /// </param>
    /// <param name="excludeProjectNames">The recorded exclusion set, carried through untouched.</param>
    /// <param name="outputFilePaths">The recorded evaluated output paths the search chooses from.</param>
    /// <param name="intermediateAssemblyPath">
    ///     The recorded <c>obj</c>-side assembly the search refuses a result under. Replayed rather than
    ///     dropped, because without it a replay answers with the intermediate a cold run refuses.
    /// </param>
    internal static SpecResolution Replay(
        string? specProjectName,
        string fallbackProjectName,
        IReadOnlyCollection<string> excludeProjectNames,
        IReadOnlyList<string>? outputFilePaths,
        string? intermediateAssemblyPath)
    {
        IReadOnlyList<string> evaluatedPaths = outputFilePaths ?? [];
        string dllPath = RequireBuiltOutput(
            specProjectName ?? fallbackProjectName, evaluatedPaths, intermediateAssemblyPath);

        return new SpecResolution(
            dllPath, specProjectName, excludeProjectNames, evaluatedPaths, intermediateAssemblyPath);
    }

    // The evaluated output paths in the one order the built-output search consumes them: blanks dropped,
    // ordinal-sorted, and idempotent. One owner because MemberResolution records for the extraction cache
    // exactly the set RequireBuiltOutput chose from — a hit replays the recorded set, so the two agreeing on
    // that shape is what keeps the replayed choice identical to the cold one.
    private static List<string> NormalizeEvaluatedPaths(IReadOnlyList<string?> outputFilePaths)
    {
        return outputFilePaths
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     One Roslyn project beside the canonicalized spelling of its <c>.csproj</c> — <see langword="null" />
    ///     where the workspace reported no project file at all, which is its own answer to every path
    ///     question and never a match.
    /// </summary>
    private sealed record CanonicalProject(Project Project, string? CanonicalFilePath);

    /// <summary>
    ///     The built-output inputs one project file yields, read together because one scan produces both:
    ///     every framework's evaluated output path (nulls included, as the workspace carried them) and the
    ///     single intermediate assembly path the search refuses an <c>obj</c>-side result under.
    /// </summary>
    private sealed record BuiltOutputInputs(
        IReadOnlyList<string?> OutputFilePaths,
        string? IntermediateAssemblyPath);
}
