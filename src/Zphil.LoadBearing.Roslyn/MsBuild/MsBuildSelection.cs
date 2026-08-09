namespace Zphil.LoadBearing.Roslyn.MsBuild;

/// <summary>
///     Result of MSBuild selection, returned by <see cref="MsBuildBootstrap.Initialize" />.
///     <see cref="Source" /> is the one-line account of which MSBuild was chosen and why.
///     <see cref="MsBuildBinPath" /> is null when falling back to
///     <see cref="Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults" /> — no Visual Studio
///     install detected — and <see cref="Version" /> is null unless the instance was discovered
///     through vswhere.
/// </summary>
// MsBuildBinPath and Version are this package's public API surface, read by consumers rather than by
// in-solution callers; Source is read through MsBuildBootstrap.LastSelection.
// ReSharper disable NotAccessedPositionalProperty.Global
public sealed record MsBuildSelection(string? MsBuildBinPath, string? Version, string Source);
