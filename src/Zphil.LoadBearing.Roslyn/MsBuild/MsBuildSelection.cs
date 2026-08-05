namespace Zphil.LoadBearing.Roslyn.MsBuild;

/// <summary>
///     Result of MSBuild selection, returned by <see cref="MsBuildBootstrap.Initialize" />.
///     <see cref="Source" /> is the one-line account of which MSBuild was chosen and why; the CLI prints
///     it beside workspace-load diagnostics, reading it back from
///     <see cref="MsBuildBootstrap.LastSelection" /> rather than from an instance, because the
///     registration that produced it happened behind a quarantine boundary.
///     <see cref="MsBuildBinPath" /> is null when falling back to
///     <see cref="Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults" /> — no Visual Studio
///     install detected.
/// </summary>
// MsBuildBinPath and Version are this package's public API surface, read by consumers rather than by
// in-solution callers; Source is read through MsBuildBootstrap.LastSelection.
// ReSharper disable NotAccessedPositionalProperty.Global
public sealed record MsBuildSelection(string? MsBuildBinPath, string? Version, string Source);
