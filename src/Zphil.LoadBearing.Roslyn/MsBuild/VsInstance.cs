namespace Zphil.LoadBearing.Roslyn.MsBuild;

/// <summary>
///     Subset of <c>vswhere</c>'s output: the fields needed for MSBuild selection.
/// </summary>
internal sealed record VsInstance(string InstallationPath, Version Version, string Name);