using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn.Extraction;

/// <summary>
///     Builds a <see cref="CodebaseModel" /> from a set of compilations by extracting each input into a
///     self-contained <see cref="Caching.CodebaseFragment" /> (<see cref="FragmentExtractor" />) and then
///     unifying the fragments by fully-qualified name (<see cref="FragmentMerger" />). This is deliberately
///     the <em>one</em> code path for cold runs, the MSBuild-free fast test path, and cache
///     hits — a hit replays persisted fragments through the same merge — so the cache cannot change results
///     by construction. See <see cref="FragmentMerger" /> for the cross-input (declare → hierarchy → edges)
///     semantics and its single documented, unobservable tie-break.
/// </summary>
internal static class CodebaseModelBuilder
{
    /// <param name="inputs">The compilations to extract, in extraction order.</param>
    /// <param name="artifactFacts">
    ///     What MSBuild said about each input's project, at the same index. Null on the paths that hand
    ///     compilations over directly, where there is no project file to evaluate.
    /// </param>
    public static CodebaseModel Build(
        IReadOnlyList<CompilationInput> inputs, IReadOnlyList<ProjectArtifactFacts?>? artifactFacts = null)
    {
        CodebaseFragment[] fragments = FragmentExtractor.ExtractAll(inputs, artifactFacts);

        return FragmentMerger.Merge(fragments);
    }
}
