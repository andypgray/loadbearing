using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn.Extraction;

/// <summary>
///     One compilation to extract from: the <see cref="Compilation" />, its project name, the names of the
///     projects it forward-references, and the target framework it was compiled for. The fast path
///     (<see cref="CodebaseExtractor.ExtractFromCompilations" />) passes an empty reference list;
///     <see cref="CodebaseExtractor.ExtractFromSolutionAsync" /> fills it from the solution graph.
/// </summary>
/// <remarks>
///     <para>
///         The target framework is <see langword="null" /> whenever it is unknown or beside the point: a
///         hand-built fast-path input, and every project of a solution whose projects each target one
///         framework. It is populated only where one project file yielded several compilations, which is the
///         case where the project name alone no longer identifies which compilation a fact came from.
///     </para>
///     <para>
///         <see cref="SolutionMember" /> says whether the solution file <em>declares</em> this project —
///         <see langword="false" /> marks a passenger a <c>ProjectReference</c> dragged into the workspace —
///         and is <see langword="null" /> wherever nothing was read to answer with, which is every
///         hand-built input and any run whose solution file would not parse.
///     </para>
///     <para>
///         <see cref="GeneratedTrees" /> is the provenance half of the generated-code signal (GRAMMAR §5.2):
///         which of <see cref="Compilation" />'s trees the workspace produced from a source generator. It
///         holds <em>trees</em> rather than paths because a generator's pseudo-path moves the moment a build
///         sets <c>EmitCompilerGeneratedFiles</c>, and it is data rather than a predicate because every other
///         member here is. <see langword="null" /> wherever nothing was loaded to answer with, which leaves
///         the banner as the only signal.
///     </para>
/// </remarks>
public sealed record CompilationInput(
    Compilation Compilation,
    string ProjectName,
    IReadOnlyList<string> ProjectReferences,
    string? TargetFramework = null,
    bool? SolutionMember = null,
    IReadOnlySet<SyntaxTree>? GeneratedTrees = null);
