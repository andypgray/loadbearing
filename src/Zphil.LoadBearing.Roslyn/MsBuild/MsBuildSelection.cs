namespace Zphil.LoadBearing.Roslyn.MsBuild;

/// <summary>
///     Result of MSBuild selection, returned by <see cref="MsBuildBootstrap.Initialize" /> (the
///     CLI logs it). <see cref="MsBuildBinPath" /> is null when falling back to
///     <see cref="Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults" /> — no Visual Studio
///     install detected.
/// </summary>
public sealed record MsBuildSelection(string? MsBuildBinPath, string? Version, string Source);