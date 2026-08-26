namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     One package a project declares, as pure data: the package identifier and the <c>file:line</c> of the
///     declaration. The serializable counterpart of
///     <see cref="Zphil.LoadBearing.Codebase.PackageReference" />.
/// </summary>
/// <remarks>
///     The declaring file is regularly not the project file — a <c>Directory.Build.props</c> above it can
///     add a package to every project beneath — which is why the site travels with the name rather than
///     being reconstructed from the project at merge.
/// </remarks>
internal readonly record struct FragmentPackageReference(string Name, FragmentSite Site);
