using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn.Extraction;

/// <summary>
///     One compilation to extract from: the <see cref="Compilation" /> itself, the project name its types are recorded
///     under, the names of the projects it references, and the target framework it was compiled for. Hand a list of
///     these to <see cref="CodebaseExtractor.ExtractFromCompilations" />, which needs no MSBuild;
///     <see cref="CodebaseExtractor.ExtractFromSolutionAsync" /> builds them from a loaded solution instead and fills
///     the reference names in from the solution itself. An input built by hand can pass an empty reference list, and
///     nothing then records edges from its project to another.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="TargetFramework" /> is <see langword="null" /> wherever it is unknown or beside the
///         point: an input built by hand, and every project of a solution whose projects each target one
///         framework. It is filled in only where one project file yielded several compilations, which is
///         exactly where the project name alone no longer says which compilation a fact came from.
///     </para>
///     <para>
///         <see cref="SolutionMember" /> says whether the solution file declares this project. <see langword="false" />
///         marks one that a <c>ProjectReference</c> dragged into the workspace, and <see langword="null" /> means
///         nothing was read to answer with: every input built by hand, any run whose solution file would not parse, and
///         a project whose file path the load never reported.
///     </para>
///     <para>
///         <see cref="GeneratedTrees" /> names which of the compilation's syntax trees came from a source
///         generator. A type counts as generated when a <c>GeneratedCodeAttribute</c> sits on it or on a
///         type containing it, or when every file declaring it is generator output — and a file is
///         generator output either because the workspace reported it as source-generated, which is what
///         this set records, or because its leading comment carries an <c>&lt;auto-generated&gt;</c>
///         banner. <see langword="null" /> leaves the banner as the only signal.
///     </para>
/// </remarks>
// GeneratedTrees holds trees by reference rather than document paths because a build that sets
// EmitCompilerGeneratedFiles moves every generated document off its pseudo-path.
public sealed record CompilationInput(
    Compilation Compilation,
    string ProjectName,
    IReadOnlyList<string> ProjectReferences,
    string? TargetFramework = null,
    bool? SolutionMember = null,
    IReadOnlySet<SyntaxTree>? GeneratedTrees = null);
