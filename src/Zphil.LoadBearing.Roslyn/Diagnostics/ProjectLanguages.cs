using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn.Diagnostics;

/// <summary>
///     The one answer to which projects this product reads. Every arm that has to skip a project the model
///     was never going to contain asks here, so widening the set is one edit rather than a hunt for the arms
///     that agreed with each other by inspection.
/// </summary>
internal static class ProjectLanguages
{
    /// <summary>Whether <paramref name="project" /> is written in the language this product extracts.</summary>
    /// <param name="project">A project of the loaded solution.</param>
    internal static bool IsCSharp(Project project)
    {
        return string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal);
    }
}
