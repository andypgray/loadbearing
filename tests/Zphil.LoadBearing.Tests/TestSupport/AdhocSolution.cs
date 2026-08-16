using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The hand-built project graph the workspace-diagnostics predicates are pinned over: an
///     <see cref="AdhocWorkspace" /> populated with <see cref="ProjectInfo" /> values, so a predicate can be
///     handed a <see cref="Solution" /> in milliseconds rather than through an MSBuild load.
/// </summary>
/// <remarks>
///     <b>The limit every consumer inherits.</b> <c>CompilationOutputInfo</c> has no public constructor, so
///     an <see cref="AdhocWorkspace" /> project's intermediate assembly path is always null — which makes
///     even <see cref="Loaded" /> the strictest case a load-failure predicate can be handed: a project
///     carrying only one of the two evaluated paths a real design-time build would have set.
/// </remarks>
internal static class AdhocSolution
{
    /// <summary>The workspace's solution with <paramref name="projects" /> added, in order.</summary>
    internal static Solution Of(AdhocWorkspace workspace, params ProjectInfo[] projects)
    {
        foreach (ProjectInfo project in projects) workspace.AddProject(project);
        return workspace.CurrentSolution;
    }

    /// <summary>Roslyn's <c>CreateEmpty</c> shape: no documents, no references, and neither output path.</summary>
    internal static ProjectInfo Empty(string name, string projectFilePath)
    {
        return ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, name, name, LanguageNames.CSharp, projectFilePath);
    }

    /// <summary>
    ///     A project the design-time build evaluated completely: <see cref="Empty" /> plus the evaluated
    ///     output path, which is what even a failed restore leaves behind.
    /// </summary>
    internal static ProjectInfo Loaded(string name, string projectFilePath)
    {
        return Empty(name, projectFilePath)
            .WithOutputFilePath(Path.Combine(Path.GetDirectoryName(projectFilePath)!, "bin", name + ".dll"));
    }
}
