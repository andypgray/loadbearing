using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     One compilation to extract from: the <see cref="Compilation" />, its project name, the names of the
///     projects it forward-references, and the target framework it was compiled for. The fast path
///     (<see cref="CodebaseExtractor.ExtractFromCompilations" />) passes an empty reference list;
///     <see cref="CodebaseExtractor.ExtractFromSolutionAsync" /> fills it from the solution graph.
/// </summary>
/// <remarks>
///     The target framework is <see langword="null" /> whenever it is unknown or beside the point: a
///     hand-built fast-path input, and every project of a solution whose projects each target one framework.
///     It is populated only where one project file yielded several compilations, which is the case where the
///     project name alone no longer identifies which compilation a fact came from.
/// </remarks>
public sealed record CompilationInput(
    Compilation Compilation,
    string ProjectName,
    IReadOnlyList<string> ProjectReferences,
    string? TargetFramework = null);
