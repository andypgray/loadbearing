using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     One candidate spec project reduced to the tuple the convention needs — so the core resolves without a
///     workspace. <see cref="ReferencePaths" /> carries the project's PE metadata reference paths <em>plus</em>
///     the output paths of its direct project references: a spec project references the contract library as a
///     package (PE metadata) once published, but as a <c>ProjectReference</c> in a source checkout, and the
///     convention must see both (the Phase 8 derive walk caught the P2P blind spot).
/// </summary>
internal sealed record SpecProjectCandidate(string Name, IReadOnlyList<string> ReferencePaths, string? OutputFilePath);

/// <summary>
///     The resolved spec: the DLL to load and, when the spec is a solution member, the project to exclude from the
///     checked universe.
/// </summary>
internal sealed record SpecResolution(string DllPath, string? ExcludeProjectName);

/// <summary>
///     Resolves which spec DLL to load (ratified decision 1, DESIGN.md §13(b) CLI half). The CLI
///     never builds: <c>--spec</c> takes a prebuilt DLL or a solution-member csproj (resolved to its
///     output DLL); with no <c>--spec</c>, the convention picks the unique solution project that
///     references <c>Zphil.LoadBearing.dll</c>. Every failure is a loud <see cref="UserErrorException" />;
///     a spec project that is a solution member is excluded from the codebase the checker sees.
/// </summary>
internal static class SpecResolver
{
    private const string CoreAssemblyFile = "Zphil.LoadBearing.dll";

    internal static SpecResolution Resolve(Solution solution, string? specArgument)
    {
        return string.IsNullOrWhiteSpace(specArgument)
            ? ResolveByConvention(solution)
            : ResolveExplicit(solution, specArgument!);
    }

    /// <summary>
    ///     The workspace-free half of resolution (R4): a built-DLL <c>--spec</c> resolves directly,
    ///     because that branch never touches the <see cref="Solution" />. Returns null when resolution
    ///     needs the workspace — the convention default (no <c>--spec</c>) and a solution-member csproj.
    ///     A DLL path that does not exist is still a loud error. This is what lets <c>explain</c> run
    ///     with no MSBuild load at all.
    /// </summary>
    internal static SpecResolution? TryResolveWithoutSolution(string? specArgument)
    {
        if (string.IsNullOrWhiteSpace(specArgument)) return null;
        if (specArgument!.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return null;

        string fullPath = Path.GetFullPath(specArgument);
        if (!File.Exists(fullPath))
            throw new UserErrorException($"--spec '{specArgument}' was not found. Pass a built spec DLL or a solution-member csproj.");

        return new SpecResolution(fullPath, null);
    }

    private static SpecResolution ResolveExplicit(Solution solution, string specArgument)
    {
        if (specArgument.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            string fullPath = Path.GetFullPath(specArgument);
            Project project = solution.Projects.FirstOrDefault(p => PathsEqual(p.FilePath, fullPath))
                              ?? throw new UserErrorException(
                                  $"--spec '{specArgument}' is not a project in the solution. " +
                                  "Pass a built spec DLL or a csproj that is a member of the target solution.");
            return new SpecResolution(RequireBuiltOutput(project.Name, project.OutputFilePath), project.Name);
        }

        // A DLL path — the branch that needs no solution.
        return TryResolveWithoutSolution(specArgument)!;
    }

    private static SpecResolution ResolveByConvention(Solution solution)
    {
        var candidates = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .Select(p => new SpecProjectCandidate(p.Name, ReferencePathsOf(p, solution), p.OutputFilePath))
            .ToList();

        SpecProjectCandidate chosen = ResolveConventionProject(candidates);
        return new SpecResolution(RequireBuiltOutput(chosen.Name, chosen.OutputFilePath), chosen.Name);
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
    ///     The pure convention core: the unique candidate that references <c>Zphil.LoadBearing.dll</c>.
    ///     Zero candidates and multiple candidates are both loud errors (unit-tested over tuples).
    /// </summary>
    internal static SpecProjectCandidate ResolveConventionProject(IReadOnlyList<SpecProjectCandidate> candidates)
    {
        var matches = candidates.Where(ReferencesCore).ToList();

        if (matches.Count == 0)
            throw new UserErrorException(
                "No spec project found: no solution project references Zphil.LoadBearing.dll. Pass --spec to name one.");

        if (matches.Count > 1)
            throw new UserErrorException(
                "Multiple spec projects found; pass --spec to disambiguate:\n  " +
                string.Join("\n  ", matches.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal)));

        return matches[0];
    }

    private static bool ReferencesCore(SpecProjectCandidate candidate)
    {
        return candidate.ReferencePaths.Any(path =>
            Path.GetFileName(path).Equals(CoreAssemblyFile, StringComparison.OrdinalIgnoreCase));
    }

    internal static string RequireBuiltOutput(string projectName, string? outputFilePath)
    {
        if (string.IsNullOrEmpty(outputFilePath) || !File.Exists(outputFilePath))
            throw new UserErrorException(
                $"The spec project '{projectName}' has no built output" +
                (string.IsNullOrEmpty(outputFilePath) ? "" : $" at '{outputFilePath}'") +
                ". Build the solution first (dotnet build).");

        return outputFilePath!;
    }

    private static bool PathsEqual(string? a, string? b)
    {
        if (a is null || b is null) return false;

        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }
}