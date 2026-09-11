using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing;

/// <summary>
///     What a project predicate sees: the facts of one project as a build artifact, handed to the
///     lambda in <c>.Where(project =&gt; ..., description)</c> and
///     <c>.Must(project =&gt; ..., description)</c> on a project selection. The facts are what a build
///     evaluation answers, not what the project file spells, so a setting supplied by a shared props
///     file above the project, or defaulted by the SDK, is a fact here and absent from the file. Where
///     nothing evaluated a project the fact is missing rather than defaulted — the lists come back
///     empty and the tri-state facts null — so a predicate that reads unknown as false asserts
///     something nothing measured; treat unknown as passing, as the project verbs do. The lambda runs
///     when the check runs, not when the spec is loaded; the description beside it is required, and it
///     is the description, never the lambda, that the generated agent context and the check report
///     print. New facts are added in later versions and the ones here keep their meaning.
/// </summary>
// Read by the predicate authors this contract exists for rather than through the interface
// in-solution, so solution-wide search finds only the implementations and calls the members unused.
// ReSharper disable UnusedMemberInSuper.Global
public interface IProjectInfo
{
    /// <summary>The project's name, as the solution lists it and as <c>Named</c> matches it.</summary>
    string Name { get; }

    /// <summary>
    ///     Every framework the project targets, as short monikers (<c>net10.0</c>, <c>netstandard2.0</c>,
    ///     and <c>net48</c> for a project that spells its framework the old way), ordered ordinally. Empty
    ///     where nothing evaluated the project.
    /// </summary>
    IReadOnlyList<string> TargetFrameworks { get; }

    /// <summary>
    ///     The packages the project declares itself, ordered by name, each carrying the file and line of
    ///     its declaration — which may be a props file above the project. Declared references only: the
    ///     packages those packages pull in are a different fact and are not here.
    /// </summary>
    IReadOnlyList<PackageReference> PackageReferences { get; }

    /// <summary>
    ///     The names of the projects this project references directly, ordered ordinally.
    /// </summary>
    IReadOnlyList<string> ProjectReferences { get; }

    /// <summary>
    ///     Whether the project produces a NuGet package, or null where nothing evaluated it.
    /// </summary>
    bool? IsPackable { get; }

    /// <summary>
    ///     Whether restoring the project writes a package lock file, or null where nothing evaluated it.
    /// </summary>
    bool? LocksPackages { get; }
}
