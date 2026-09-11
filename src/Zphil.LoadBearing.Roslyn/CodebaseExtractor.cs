using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Builds the <see cref="CodebaseModel" /> a check reads — the types a codebase declares, the members
///     they declare, and the <c>file:line</c> edges between them — out of Roslyn compilations. Use
///     <see cref="ExtractFromSolutionAsync" /> for a solution loaded by <see cref="WorkspaceLoader" /> or a
///     <see cref="WorkspaceSession" />, or <see cref="ExtractFromCompilations" /> for compilations you
///     built yourself, which needs no MSBuild at all.
/// </summary>
public static class CodebaseExtractor
{
    /// <summary>
    ///     Extracts the model from compilations you built yourself, with no MSBuild involved. Every input's
    ///     types are declared before any reference is resolved, so a type one compilation references and
    ///     another declares is one node in the model, matched by fully-qualified name. Where two inputs declare
    ///     the same type, the first in <paramref name="inputs" /> supplies its facts.
    /// </summary>
    /// <param name="inputs">The compilations to extract from, in the order they are declared.</param>
    public static CodebaseModel ExtractFromCompilations(IReadOnlyList<CompilationInput> inputs)
    {
        return CodebaseModelBuilder.Build(inputs);
    }

    /// <summary>
    ///     Extracts the model from a loaded solution: its C# projects in ordinal name order, each with its
    ///     compilation and the names of the projects it references. Register MSBuild with
    ///     <see cref="MsBuild.MsBuildBootstrap.EnsureInitialized" /> and load the solution first, through
    ///     <see cref="WorkspaceLoader" /> or a <see cref="WorkspaceSession" />; restore or build it before that,
    ///     because a project whose packages were never restored loads without them and the types it gets from
    ///     them are simply absent from the model.
    /// </summary>
    /// <param name="solution">The loaded solution.</param>
    /// <param name="excludeProjects">
    ///     Project names to leave out of the model — the way a spec project that is itself a member of the
    ///     solution stays out of its own check. Null excludes nothing.
    /// </param>
    /// <param name="targetFrameworks">
    ///     The per-project target frameworks the load reported
    ///     (<see cref="LoadedSolution.TargetFrameworks" />), so a fact taken from a multi-target-framework
    ///     project records the framework it came from. Null, or a project absent from it, leaves the framework
    ///     unstamped, which is the single-framework norm.
    /// </param>
    /// <param name="declaredMembers">
    ///     The absolute <c>.csproj</c> paths the solution file itself declares, symlink-resolved, stamped onto
    ///     each project as <see cref="ProjectNode.SolutionMember" /> so that a project a
    ///     <c>ProjectReference</c> dragged into the workspace can be told from one the solution claims. Null
    ///     leaves every project unlabeled, which is what an unreadable solution file has to degrade to.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    // MethodImplOptions.NoInlining keeps the JIT from resolving Roslyn types before MSBuildLocator
    // registration in non-test hosts.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<CodebaseModel> ExtractFromSolutionAsync(
        Solution solution,
        IReadOnlyCollection<string>? excludeProjects = null,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
        IReadOnlySet<string>? declaredMembers = null,
        CancellationToken ct = default)
    {
        ExtractionInputs collected = await CollectInputsAsync(
            solution, p => excludeProjects is null || !excludeProjects.Contains(p.Name), targetFrameworks,
            declaredMembers, ct);
        return CodebaseModelBuilder.Build(collected.Inputs, collected.ArtifactFacts);
    }

    /// <summary>
    ///     Extracts one self-contained <see cref="CodebaseFragment" /> per C# project matched by
    ///     <paramref name="includeProjects" /> (null extracts every C# project), in the same ordinal
    ///     project order and with the same per-project <see cref="CompilationInput" />s the merge-producing
    ///     path uses — so the fragments a workspace-loaded run persists and the fragments a
    ///     cache hit replays go through the identical <see cref="FragmentMerger" />, and the cache cannot
    ///     change results by construction. The extraction cache calls this with <c>null</c> on a miss
    ///     (extract all) and with just the dirty projects on a partial (reuse the clean fragments).
    /// </summary>
    /// <remarks>
    ///     <see cref="MethodImplOptions.NoInlining" /> keeps the JIT from resolving Roslyn types before
    ///     <c>MSBuildLocator</c> registration in non-test hosts, matching <see cref="ExtractFromSolutionAsync" />.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<IReadOnlyList<CodebaseFragment>> ExtractFragmentsAsync(
        Solution solution,
        IReadOnlyCollection<string>? includeProjects,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
        IReadOnlySet<string>? declaredMembers = null,
        CancellationToken ct = default)
    {
        ExtractionInputs collected = await CollectInputsAsync(
            solution, p => includeProjects is null || includeProjects.Contains(p.Name), targetFrameworks,
            declaredMembers, ct);
        return FragmentExtractor.ExtractAll(collected.Inputs, collected.ArtifactFacts);
    }

    // The shared project enumeration behind both entry points: C# projects passing the filter, ordered by
    // (name, target framework), each turned into a CompilationInput carrying its forward project-reference
    // names and its framework. One enumeration means the extract-all-then-merge cold path, the
    // extract-fragments cache path, and cache hits all order identically.
    //
    // The framework tiebreak is load-bearing, not cosmetic. One project file's several compilations now share
    // a name, so ordering by name alone would leave them in whatever order the solution enumerated them —
    // <TargetFrameworks> declaration order — and the merge gives the FIRST input's facts to every type they
    // share. Ordering on the framework fixes which one that is, so reordering a csproj's framework list
    // cannot silently move a type's facts.
    private static async Task<ExtractionInputs> CollectInputsAsync(
        Solution solution,
        Func<Project, bool> include,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks,
        IReadOnlySet<string>? declaredMembers,
        CancellationToken ct)
    {
        List<Project> projects = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .Where(include)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ThenBy(p => TargetFrameworkOf(targetFrameworks, p) ?? "", StringComparer.Ordinal)
            .ToList();

        ct.ThrowIfCancellationRequested();

        // The artifact facts come from MSBuild rather than from Roslyn, so this batch shares nothing with the
        // binding below and runs beside it: evaluation is a serial walk of one ProjectCollection, and binding
        // is the expensive half it would otherwise wait behind. One request per input, in input order, so the
        // results read back by the same index as everything else here.
        List<ProjectEvaluationRequest> evaluationRequests = projects
            .Select(project => new ProjectEvaluationRequest(project.FilePath, TargetFrameworkOf(targetFrameworks, project)))
            .ToList();
        Task<IReadOnlyList<ProjectArtifactFacts?>> artifactFactsTask =
            Task.Run(() => ProjectFactsEvaluator.EvaluateAll(evaluationRequests, ct), ct);

        // Binding is the expensive half and the projects are independent, so they bind together rather than
        // one after another. The ordered list above still decides input order — the results are read back by
        // index, never in completion order — so the merge's first-declarer-wins rule sees what it always did.
        IEnumerable<Task<Compilation?>> compilationTasks = projects.Select(project => project.GetCompilationAsync(ct));
        Compilation?[] compilations = await Task.WhenAll(compilationTasks);

        // The provenance half of the generated-code signal (GRAMMAR §5.2), read once per project. It runs
        // AFTER the compilation batch rather than beside it because binding is what executes the generators:
        // by here every generated document is realized, so this batch is a cache read rather than a second
        // round of generator work.
        IEnumerable<Task<IReadOnlySet<SyntaxTree>>> generatedTreeTasks =
            projects.Select(project => GeneratedTreesOfAsync(project, ct));
        IReadOnlySet<SyntaxTree>[] generatedTrees = await Task.WhenAll(generatedTreeTasks);

        // One canonicalizer for the whole enumeration: every project's membership is tested against a
        // canonicalized path, and the projects of one solution share nearly all of their ancestors.
        var canonicalProjectFiles = new ProjectFileCanonicalizer();

        IReadOnlyList<ProjectArtifactFacts?> evaluated = await artifactFactsTask;

        List<CompilationInput> inputs = [];
        List<ProjectArtifactFacts?> artifactFacts = [];
        for (var i = 0; i < projects.Count; i++)
        {
            if (compilations[i] is not { } compilation) continue;

            Project project = projects[i];
            List<string> projectReferences = project.ProjectReferences
                .Select(r => solution.GetProject(r.ProjectId)?.Name)
                .Where(n => n is not null)
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            inputs.Add(new CompilationInput(
                compilation, project.Name, projectReferences, TargetFrameworkOf(targetFrameworks, project),
                SpecExclusion.SolutionMembershipOf(
                    declaredMembers, project.FilePath, canonicalProjectFiles.Resolve(project.FilePath)),
                generatedTrees[i]));
            artifactFacts.Add(evaluated[i]);
        }

        return new ExtractionInputs(inputs, artifactFacts);
    }

    // The trees one project's source generators produced, by reference — the identity the extractor tests
    // each declaring tree against. Holding trees rather than the documents' paths is what survives a build
    // that sets EmitCompilerGeneratedFiles, which moves every generated document off its pseudo-path.
    private static async Task<IReadOnlySet<SyntaxTree>> GeneratedTreesOfAsync(Project project, CancellationToken ct)
    {
        IEnumerable<SourceGeneratedDocument> documents = await project.GetSourceGeneratedDocumentsAsync(ct);

        var trees = new HashSet<SyntaxTree>();
        foreach (SourceGeneratedDocument document in documents)
            if (await document.GetSyntaxTreeAsync(ct) is { } tree)
                trees.Add(tree);

        return trees;
    }

    private static string? TargetFrameworkOf(IReadOnlyDictionary<ProjectId, string>? targetFrameworks, Project project)
    {
        if (targetFrameworks is null) return null;

        return targetFrameworks.GetValueOrDefault(project.Id);
    }
}
