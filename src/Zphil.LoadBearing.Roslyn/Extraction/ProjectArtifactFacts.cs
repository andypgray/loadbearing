using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn.Extraction;

/// <summary>
///     What one evaluation of one project file said about the artifact it builds: the frameworks it
///     declares, the packages it declares, whether it packs, and whether its restore locks — each paired
///     with the <c>file:line</c> that decided it.
/// </summary>
/// <remarks>
///     <para>
///         One value per <em>compilation</em>, not per project file: a multi-targeted project is evaluated
///         once per framework, because a condition on <c>$(TargetFramework)</c> can add a package to one
///         framework and not another, and an outer-build evaluation would see neither. The merge folds the
///         per-framework values back into one project.
///     </para>
///     <para>
///         Absence of the whole value means no evaluation happened. Absence <em>inside</em> it means
///         something narrower and is documented per member, because the two spellings of "no" are not the
///         same fact: an undeclared <c>RestorePackagesWithLockFile</c> genuinely says restore does not lock,
///         while an undefined <c>IsPackable</c> says this project is outside the machinery that would answer.
///     </para>
/// </remarks>
/// <param name="TargetFrameworks">
///     Every framework the project declares, ordinal-ordered and normalized to the short moniker. Empty
///     where the project declares none this reader recognises.
/// </param>
/// <param name="TargetFrameworksSite">Where the frameworks are declared; the project file where nothing declares them.</param>
/// <param name="PackageReferences">
///     The packages the project declares, ordinal by name — never the implicit references the SDK adds, and
///     never the transitive closure.
/// </param>
/// <param name="IsPackable">
///     Whether the project packs, or <see langword="null" /> where the property is undefined — a project
///     that imports no pack targets has no answer rather than a negative one.
/// </param>
/// <param name="IsPackableSite">
///     Where packability was decided; the project file where the winning declaration is not
///     local.
/// </param>
/// <param name="LocksPackages">Whether restore writes a lock file. Undeclared reads <see langword="false" />.</param>
/// <param name="LocksPackagesSite">Where the lock policy was decided; the project file where nothing declares it.</param>
internal sealed record ProjectArtifactFacts(
    IReadOnlyList<string> TargetFrameworks,
    FragmentSite TargetFrameworksSite,
    IReadOnlyList<FragmentPackageReference> PackageReferences,
    bool? IsPackable,
    FragmentSite IsPackableSite,
    bool LocksPackages,
    FragmentSite LocksPackagesSite);

/// <summary>
///     What one enumeration of a solution produced: the compilations to extract from, and what MSBuild said
///     about each of their projects — one entry per input, at the same index.
/// </summary>
/// <remarks>
///     Two lists rather than one field on <see cref="CompilationInput" />, because the facts come from a
///     different engine than the compilation does and are absent on every path that hands compilations over
///     directly. Pairing them here keeps that asymmetry at the one place that knows about both.
/// </remarks>
/// <param name="Inputs">The compilations, in extraction order.</param>
/// <param name="ArtifactFacts">
///     The evaluated facts for each input's project, by index; <see langword="null" /> where that project
///     could not be evaluated.
/// </param>
internal sealed record ExtractionInputs(
    IReadOnlyList<CompilationInput> Inputs,
    IReadOnlyList<ProjectArtifactFacts?> ArtifactFacts);
