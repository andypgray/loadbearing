using System.Runtime.InteropServices;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     The one per-OS rule for comparing file-system path segments: case-insensitive on Windows and
///     macOS, ordinal (case-sensitive) on Linux — the file-name reality of each platform. Shared by
///     every path compare across the codebase (<see cref="PathFormat" />, the diff tripwire, scope
///     placement, AGENTS.md dedupe, spec resolution) so they cannot drift apart. Core is
///     netstandard2.0, so the platform test uses <see cref="RuntimeInformation.IsOSPlatform" /> rather
///     than the net5+ <c>OperatingSystem.IsWindows()</c> helpers.
/// </summary>
public static class PathComparison
{
    private static readonly bool CaseInsensitive =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>The <see cref="StringComparison" /> for path segments on this OS.</summary>
    public static readonly StringComparison Comparison =
        CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The matching <see cref="StringComparer" /> for path-keyed sets and dictionaries on this OS.</summary>
    public static readonly StringComparer Comparer =
        CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}