using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     One candidate spec project reduced to the tuple the convention needs — so the core resolves without a
///     workspace.
/// </summary>
/// <remarks>
///     <see cref="ReferencePaths" /> carries the project's PE metadata reference paths <em>plus</em>
///     the output paths of its direct project references: a spec project references the contract library as a
///     package (PE metadata) once published, but as a <c>ProjectReference</c> in a source checkout, and the
///     convention must see both (the derive walk caught the P2P blind spot).
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
internal sealed record SpecResolution(
    string DllPath,
    string? SpecProjectName,
    IReadOnlyCollection<string> ExcludeProjectNames);

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
    /// <param name="solutionPath">The solution file, read for its declared membership.</param>
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
        Solution solution, string solutionPath, string? specArgument, WorkspaceDiagnostics diagnostics)
    {
        return string.IsNullOrWhiteSpace(specArgument)
            ? ResolveByConvention(solution, solutionPath, diagnostics)
            : ResolveExplicit(solution, solutionPath, specArgument);
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

    private static SpecResolution ResolveExplicit(Solution solution, string solutionPath, string specArgument)
    {
        if (specArgument.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            string fullPath = Path.GetFullPath(specArgument);
            Project project = solution.Projects.FirstOrDefault(p => PathsEqual(p.FilePath, fullPath))
                              ?? throw new UserErrorException(
                                  $"--spec '{specArgument}' is not a project in the solution. " +
                                  "Pass a built spec DLL or a csproj that is a member of the target solution.");
            var declaredMembers = SpecExclusion.TryReadDeclaredMembers(solutionPath);
            var outputFilePaths = OutputsOfProjectFile(solution, project.FilePath, project.OutputFilePath);
            string? intermediateAssemblyPath =
                IntermediateOfProjectFile(solution, project.FilePath, project.CompilationOutputInfo.AssemblyPath);
            return MemberResolution(
                solution, declaredMembers, project.Name, outputFilePaths, intermediateAssemblyPath);
        }

        // A DLL path — the branch that needs no solution.
        return TryResolveWithoutSolution(specArgument)!;
    }

    private static SpecResolution ResolveByConvention(
        Solution solution, string solutionPath, WorkspaceDiagnostics diagnostics)
    {
        // Read declared membership once: the convention filters candidates by it, and the exclusion walk
        // subtracts the same set.
        var declaredMembers = SpecExclusion.TryReadDeclaredMembers(solutionPath);

        var candidates = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .Select(p => new SpecProjectCandidate(
                p.Name,
                ReferencePathsOf(p, solution),
                p.OutputFilePath,
                p.FilePath,
                SpecExclusion.IsDeclaredMember(declaredMembers, p.FilePath),
                p.CompilationOutputInfo.AssemblyPath))
            .ToList();

        SpecProjectCandidate chosen = ResolveConventionProject(candidates, diagnostics);
        var outputFilePaths = OutputsOfProjectFile(solution, chosen.FilePath, chosen.OutputFilePath);
        string? intermediateAssemblyPath =
            IntermediateOfProjectFile(solution, chosen.FilePath, chosen.IntermediateAssemblyPath);
        return MemberResolution(
            solution, declaredMembers, chosen.Name, outputFilePaths, intermediateAssemblyPath);
    }

    // The shared tail of both solution-member branches: the built DLL plus the projects the checked universe
    // drops — the spec project and the plumbing only it references. A null declaredMembers is the unreadable
    // case, which SpecExclusion degrades to just the spec project.
    private static SpecResolution MemberResolution(
        Solution solution,
        IReadOnlySet<string>? declaredMembers,
        string specProjectName,
        IReadOnlyList<string?> outputFilePaths,
        string? intermediateAssemblyPath)
    {
        return new SpecResolution(
            RequireBuiltOutput(specProjectName, outputFilePaths, intermediateAssemblyPath),
            specProjectName,
            SpecExclusion.Compute(solution, declaredMembers, specProjectName));
    }

    // Every output path the projects sharing one project file evaluate to. A multi-target-framework csproj
    // yields one Roslyn project per framework, so any single project's OutputFilePath names an arbitrary
    // framework's DLL; the built-output check needs them all so it can pick one that is actually on disk —
    // and so a cache hit, which records the same set, replays the identical choice.
    private static IReadOnlyList<string?> OutputsOfProjectFile(
        Solution solution, string? projectFilePath, string? evaluatedOutputFilePath)
    {
        if (string.IsNullOrEmpty(projectFilePath)) return [evaluatedOutputFilePath];

        return solution.Projects
            .Where(p => PathsEqual(p.FilePath, projectFilePath))
            .Select(p => p.OutputFilePath)
            .ToList();
    }

    // The one intermediate assembly path to hand the built-output search, out of however many the projects
    // sharing a project file carry. One suffices even for a multi-target-framework spec project: the search
    // derives the intermediate ROOT by peeling the prefix the evaluated and intermediate paths share, and the
    // two diverge at the same segment level whichever framework's pair it starts from — so the derived root
    // is framework-invariant. Ordinal-first keeps the choice deterministic rather than load-order dependent.
    private static string? IntermediateOfProjectFile(
        Solution solution, string? projectFilePath, string? evaluatedIntermediatePath)
    {
        if (string.IsNullOrEmpty(projectFilePath)) return evaluatedIntermediatePath;

        return solution.Projects
            .Where(p => PathsEqual(p.FilePath, projectFilePath))
            .Select(p => p.CompilationOutputInfo.AssemblyPath)
            .Where(path => !string.IsNullOrEmpty(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    ///     A project's reference paths as the convention sees them: PE metadata references (the package /
    ///     built-DLL shape) plus the output paths of direct project references (the source-checkout shape,
    ///     where the contract library arrives as a <c>ProjectReference</c> and never appears among the PE
    ///     metadata references).
    /// </summary>
    private static IReadOnlyList<string> ReferencePathsOf(Project project, Solution solution)
    {
        var metadataPaths = project.MetadataReferences
            .OfType<PortableExecutableReference>()
            .Select(r => r.FilePath ?? string.Empty);

        var projectReferenceOutputs = project.ProjectReferences
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
        var matches = candidates.Where(c => c.IsDeclaredMember && ReferencesCore(c)).ToList();

        if (matches.Count == 0) throw NoSpecProjectFound(candidates, diagnostics);

        var groups = matches
            .Select((candidate, index) => (Candidate: candidate, Key: GroupKey(candidate, index)))
            .GroupBy(match => match.Key, match => match.Candidate, StringComparer.Ordinal)
            .ToList();

        if (groups.Count > 1)
        {
            var lines = groups
                .Select(group => DisambiguationLine(group.First()))
                .OrderBy(line => line, StringComparer.Ordinal);

            throw new UserErrorException(
                "Multiple spec projects found; pass --spec to disambiguate:\n  " + string.Join("\n  ", lines));
        }

        return matches[0];
    }

    // Zero candidates has causes whose remedies do not overlap, and reporting only the first sent readers to
    // write an argument that could not help them. Three arms, strongest evidence first:
    //
    //  1. Projects failed to load. The one that would have matched may be among them, and no --spec argument
    //     repairs a load — so this arm names them and points at the build.
    //  2. Nothing failed to load, but the load reported problems about this solution. That is the measured
    //     locked-mode shape: a broken restore leaves the spec project's package reference unresolved while
    //     the project itself still loads, so the convention finds nothing and the reader must repair the
    //     restore rather than name a project. It says "may", because that is all it knows.
    //  3. A clean load means the solution really has no spec project. The sentence stays byte-identical (the
    //     derive_spec prompt quotes it), plus how many projects were considered, which is what tells a reader
    //     whether the workspace held what they expected.
    //
    // NuGetAudit advisories are excluded from arm 2's input, and only from arm 2's: an advisory's publication
    // date says nothing about whether this solution's references resolved, so a solution that genuinely has
    // no spec project must not be sent to go and fix its restore. Nothing here gates — arms 1 and 2 are the
    // same refusal with different evidence — so no exit code turns on that distinction.
    private static UserErrorException NoSpecProjectFound(
        IReadOnlyList<SpecProjectCandidate> candidates, WorkspaceDiagnostics diagnostics)
    {
        var lines = new List<string>();
        if (diagnostics.IsIncomplete)
        {
            lines.Add(
                "No spec project found: one or more projects failed to load, so a project that references "
                + "Zphil.LoadBearing.dll may be among them:");
            lines.AddRange(Quoted(diagnostics.FailedProjects));
        }
        else if (diagnostics.ActionableDiagnostics.Count > 0)
        {
            lines.Add(
                "No spec project found: the workspace did not load cleanly, so a project that references "
                + "Zphil.LoadBearing.dll may have failed to resolve it:");
            lines.AddRange(Quoted(diagnostics.ActionableDiagnostics));
        }
        else
        {
            return new UserErrorException(
                "No spec project found: no solution project references Zphil.LoadBearing.dll. Pass --spec to name one.\n"
                + $"Considered {candidates.Count} C# project(s) in the workspace.");
        }

        lines.Add("Restore and build the solution first (dotnet restore, dotnet build), then retry.");

        // The refusal is thrown before a CodebaseSource exists, so no runner renders the evidence beside it —
        // the message has to carry it itself. CliErrorMapper.Write splits on \n and writes a line apiece, so
        // this reads the same on stderr and in an MCP error result.
        return new UserErrorException(string.Join("\n", lines));
    }

    // Up to MaxQuotedDiagnostics entries, two-space indented as every other evidence block renders them, then
    // a count of the rest.
    private static IEnumerable<string> Quoted(IReadOnlyList<string> evidence)
    {
        foreach (string entry in evidence.Take(MaxQuotedDiagnostics)) yield return "  " + entry;

        if (evidence.Count > MaxQuotedDiagnostics)
            yield return $"  ... and {evidence.Count - MaxQuotedDiagnostics} more.";
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

    private static bool ReferencesCore(SpecProjectCandidate candidate)
    {
        return candidate.ReferencePaths.Any(path =>
            Path.GetFileName(path).Equals(CoreAssemblyFile, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     The one built output the spec loads from, given the single path a project evaluated to. Delegates
    ///     to the list overload, which is where the resolution and the error text live.
    /// </summary>
    internal static string RequireBuiltOutput(
        string projectName, string? outputFilePath, string? intermediateAssemblyPath = null)
    {
        return RequireBuiltOutput(projectName, [outputFilePath], intermediateAssemblyPath);
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
        var evaluatedPaths = outputFilePaths
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        foreach (string outputFilePath in evaluatedPaths)
        {
            // The evaluated path exists: it is the answer, with no search and — deliberately — no
            // intermediate refusal. Binlog replay hands an obj-side assembly as this primary argument
            // (BinlogReplayer.NormalizeProjects), because a capture records no other path, so refusing an
            // intermediate here would refuse every replayed run.
            if (File.Exists(outputFilePath)) return outputFilePath;

            // The evaluated path can name a directory no build ever writes, in any configuration:
            // MSBuildWorkspace evaluates in its default configuration whatever the caller built, and a
            // props file spelling `bin\$(Configuration)\` is imported before the SDK defaults Configuration
            // at all, evaluating to a flat `bin\`. So search the tree the build actually wrote, from the
            // SDK's own output root down, rather than doing arithmetic over a path shape we did not build.
            if (BuiltOutputProbe.Find(outputFilePath, intermediateAssemblyPath) is { } built) return built;
        }

        throw new UserErrorException(
            $"The spec project '{projectName}' has no built output" +
            (evaluatedPaths.Count == 0 ? "" : $" at '{string.Join("' or '", evaluatedPaths)}'") +
            ". Build the solution first (dotnet build).");
    }

    // Compares a user-supplied --spec csproj path against a Roslyn project.FilePath. Both are
    // canonicalized (symlinks resolved) so a solution opened through a symlinked root still matches, and
    // compared per-OS (PathComparison) so the match neither misses across symlink spellings nor
    // over-matches case-variant paths on a case-sensitive file system.
    private static bool PathsEqual(string? a, string? b)
    {
        if (a is null || b is null) return false;

        return string.Equals(PathCanonicalizer.Resolve(a), PathCanonicalizer.Resolve(b), PathComparison.Comparison);
    }
}
