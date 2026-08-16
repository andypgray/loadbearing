using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn;

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
    public static CodebaseModel Build(IReadOnlyList<CompilationInput> inputs)
    {
        CodebaseFragment[] fragments = FragmentExtractor.ExtractAll(inputs);

        return FragmentMerger.Merge(fragments);
    }
}
