using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing;

/// <summary>
///     The read-only view of a project handed to the project escape-hatch predicates
///     (<c>.Where(pred, ...)</c> and <c>.Must(pred, ...)</c> on a project selection). This is the v1
///     predicate input contract for the project stratum (GRAMMAR §5.6); it grows additively as extraction
///     learns new artifact facts. Predicates are stored on the model but never evaluated at spec build —
///     the mandatory description is what renders, not the lambda.
/// </summary>
/// <remarks>
///     Every fact here is <em>evaluated</em> rather than read out of the project file's XML, so the
///     tri-state members are <see cref="bool" />? and <see langword="null" /> means the evaluation never
///     happened rather than that the property is off. A predicate that treats unknown as false is
///     therefore asserting something the model never measured; treat unknown as passing, exactly as the
///     shipped verbs do.
/// </remarks>
// Read by the predicate authors this contract exists for rather than through the interface
// in-solution, so solution-wide search finds only the implementations and calls the members unused.
// ReSharper disable UnusedMemberInSuper.Global
public interface IProjectInfo
{
    /// <summary>The project (assembly) name.</summary>
    string Name { get; }

    /// <summary>
    ///     Every target framework the project declares, ordinal-ordered and normalized to the short
    ///     moniker. Empty where nothing evaluated the project.
    /// </summary>
    IReadOnlyList<string> TargetFrameworks { get; }

    /// <summary>
    ///     The packages the project <em>declares</em>, ordinal by name, each with the declaration site.
    ///     Declared references only — the transitive package graph is a different fact this model does
    ///     not hold.
    /// </summary>
    IReadOnlyList<PackageReference> PackageReferences { get; }

    /// <summary>The names of the projects this project references, ordinal-ordered.</summary>
    IReadOnlyList<string> ProjectReferences { get; }

    /// <summary>Whether the project produces a package, or null where nothing answered the question.</summary>
    bool? IsPackable { get; }

    /// <summary>Whether restoring the project writes a lock file, or null where nothing evaluated it.</summary>
    bool? LocksPackages { get; }
}
