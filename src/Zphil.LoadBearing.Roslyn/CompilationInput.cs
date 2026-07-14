using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     One compilation to extract from: the <see cref="Compilation" />, its project name, and the
///     names of the projects it forward-references. The fast path
///     (<see cref="CodebaseExtractor.ExtractFromCompilations" />) passes an empty reference list;
///     <see cref="CodebaseExtractor.ExtractFromSolutionAsync" /> fills it from the solution graph.
/// </summary>
public sealed record CompilationInput(
    Compilation Compilation,
    string ProjectName,
    IReadOnlyList<string> ProjectReferences);