using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The extraction cache's view of a loaded <see cref="Solution" />: <see cref="SolutionProjects" />'
///     per-project collapse with the one thing that is this cache's own — which documents it tracks.
/// </summary>
/// <remarks>
///     <para>
///         <b>Generated documents under <c>bin</c>/<c>obj</c> are deliberately not tracked.</b> A project's
///         document set includes MSBuild-generated sources (<c>*.AssemblyInfo.cs</c>, <c>*.GlobalUsings.g.cs</c>,
///         analyzer/source-generator output), whose mtime — and sometimes bytes — churn on every design-time
///         build even when nothing the model depends on has changed. Their content is a pure function of the
///         structural inputs already fingerprinted (the project file, its assets, and the on-disk source), so a
///         change that reaches the model reaches the fingerprint through those inputs and fingerprinting the
///         output too would only manufacture false dirties on any actively-built solution (LoadBearing's own
///         repo included). Generator output does declare types — the regex classes a <c>[GeneratedRegex]</c>
///         method emits are in the assembly, and a project noun names them (GRAMMAR §4.1) — so this exclusion
///         rests on the derivation, never on the output being empty. Excluding them also aligns with the
///         store's cone scan, which already skips <c>bin</c>/<c>obj</c>.
///     </para>
/// </remarks>
internal static class SolutionCacheInputs
{
    /// <summary>Collects one <see cref="ProjectInputs" /> per C# project, in ordinal name order.</summary>
    internal static IReadOnlyList<ProjectInputs> Collect(Solution solution)
    {
        return SolutionProjects.Collect(
            solution,
            static (projectDirectory, documentPath) =>
                !BuildOutputDirectories.IsUnderBuildOutput(projectDirectory, documentPath));
    }
}
