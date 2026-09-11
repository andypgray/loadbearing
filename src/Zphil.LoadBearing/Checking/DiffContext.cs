using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     The files a check is to treat as changed. Pass one to a <c>Check</c> call on
///     <see cref="ArchChecker" /> and a scope's tripwire fires: it warns once for each of these files that
///     declares a type inside a quarantined or a cautioned scope, and the rule passes either way. Pass none
///     and every tripwire is skipped instead. Paths may arrive with either separator, and are matched the
///     way the host file system matches names — ignoring case on Windows and macOS, exactly on Linux.
/// </summary>
// Pure string logic: no Path.GetRelativePath and no Span, neither being available on netstandard2.0.
public sealed class DiffContext
{
    private readonly HashSet<string> _changed;
    private readonly string _solutionPrefix;

    /// <summary>
    ///     Builds a diff context from the solution directory and the files that changed. Paths may use
    ///     either separator and any casing; they are stored with forward slashes and keep the casing given.
    /// </summary>
    /// <param name="solutionDirectory">The directory the paths in a tripwire's warning are made relative to.</param>
    /// <param name="changedFiles">The files the check is to treat as changed.</param>
    public DiffContext(string solutionDirectory, IEnumerable<string> changedFiles)
    {
        SolutionDirectory = Normalize(Guard.NotNull(solutionDirectory, nameof(solutionDirectory))).TrimEnd('/');
        _solutionPrefix = SolutionDirectory + "/";
        _changed = new HashSet<string>(PathComparison.Comparer);
        foreach (string file in Guard.NotNull(changedFiles, nameof(changedFiles))) _changed.Add(Normalize(file));
        ChangedFiles = _changed;
    }

    /// <summary>
    ///     Gets the solution directory the context was built with, its separators normalized to forward
    ///     slashes and any trailing slash removed.
    /// </summary>
    public string SolutionDirectory { get; }

    /// <summary>
    ///     Gets the changed files with forward slashes and their original casing, in no particular order.
    ///     Membership follows the host file system's name comparison: case is ignored on Windows and macOS,
    ///     and is significant on Linux.
    /// </summary>
    public IReadOnlyCollection<string> ChangedFiles { get; }

    /// <summary>
    ///     Whether <paramref name="filePath" /> is one of the changed files. Separator-insensitive
    ///     always; case-insensitive only where the OS file system is (<see cref="PathComparison" />).
    /// </summary>
    internal bool Contains(string filePath)
    {
        return _changed.Contains(Normalize(filePath));
    }

    /// <summary>
    ///     The solution-relative, forward-slash form of <paramref name="filePath" /> for a tripwire
    ///     message; returns the normalized path unchanged when it is not under the solution directory.
    /// </summary>
    internal string SolutionRelative(string filePath)
    {
        string normalized = Normalize(filePath);
        return normalized.StartsWith(_solutionPrefix, PathComparison.Comparison)
            ? normalized.Substring(_solutionPrefix.Length)
            : normalized;
    }

    // Backslashes → forward slashes; no case folding here (the set and SolutionRelative both compare
    // with the per-OS PathComparison), so the stored path keeps its original casing.
    private static string Normalize(string path)
    {
        return path.Replace('\\', '/');
    }
}
