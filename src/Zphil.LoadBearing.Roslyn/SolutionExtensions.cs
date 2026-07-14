using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Extension methods for <see cref="Solution" />.
/// </summary>
internal static class SolutionExtensions
{
    /// <summary>
    ///     Removes <see cref="UnresolvedAnalyzerReference" /> and
    ///     <see cref="UnresolvedMetadataReference" /> instances from all projects, returning the
    ///     cleaned solution and counts of each removed.
    /// </summary>
    /// <remarks>
    ///     Called once at solution load. Unresolved analyzer references crash Roslyn cross-project
    ///     traversal APIs (SymbolFinder, Renamer) with a switch-expression failure; unresolved
    ///     metadata references are stripped defensively for the same reason. This is a read-only
    ///     transform — the returned <see cref="Solution" /> is carried forward, never applied back to
    ///     the workspace, so csproj files on disk are left untouched.
    /// </remarks>
    public static (Solution Solution, int AnalyzerCount, int MetadataCount) StripUnresolvedReferences(this Solution solution)
    {
        var analyzerCount = 0;
        var metadataCount = 0;

        foreach (Project project in solution.Projects.ToList())
        {
            foreach (AnalyzerReference analyzerRef in project.AnalyzerReferences)
                if (analyzerRef is UnresolvedAnalyzerReference)
                {
                    solution = solution.RemoveAnalyzerReference(project.Id, analyzerRef);
                    analyzerCount++;
                }

            foreach (MetadataReference metadataRef in project.MetadataReferences)
                if (metadataRef is UnresolvedMetadataReference)
                {
                    solution = solution.RemoveMetadataReference(project.Id, metadataRef);
                    metadataCount++;
                }
        }

        return (solution, analyzerCount, metadataCount);
    }
}