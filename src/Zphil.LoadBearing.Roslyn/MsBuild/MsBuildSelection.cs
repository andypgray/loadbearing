namespace Zphil.LoadBearing.Roslyn.MsBuild;

/// <summary>
///     Which MSBuild a run is using, returned by <see cref="MsBuildBootstrap.Initialize" /> and
///     <see cref="MsBuildBootstrap.EnsureInitialized" />. <see cref="Source" /> is a one-line account of what was
///     chosen and why, meant to be logged or printed: it is what answers "which MSBuild did this run use" on a machine
///     nobody can attach a debugger to. <see cref="MsBuildBinPath" /> is the <c>MSBuild\Current\Bin</c> directory of
///     the Visual Studio install that was selected, or <see langword="null" /> where none was selected and
///     <see cref="Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults" /> supplied the engine on its own.
///     <see cref="Version" /> is the version of the Visual Studio install that was selected, where it was discovered on
///     this machine, and <see langword="null" /> otherwise: an install named by <c>LOADBEARING_VS_INSTALL_PATH</c> is
///     used without its version being read.
/// </summary>
// MsBuildBinPath and Version are this package's public API surface, read by consumers rather than by
// in-solution callers; Source is read through MsBuildBootstrap.LastSelection.
// ReSharper disable NotAccessedPositionalProperty.Global
public sealed record MsBuildSelection(string? MsBuildBinPath, string? Version, string Source);
