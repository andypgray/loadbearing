using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     The set of files changed relative to a git ref — the substrate the Freeze tripwire checks
///     against (GRAMMAR §7). Paths are normalized to forward slashes on the way in and compared with the
///     platform's file-name comparison (<see cref="PathComparison" />: case-insensitive on Windows and
///     macOS, ordinal on Linux); <see cref="Contains" /> answers "was this declaration-site file
///     changed", and <see cref="SolutionRelative" /> renders the agent-facing path in a tripwire
///     warning. Built by the CLI's git integration and passed to
///     <see cref="ArchChecker.Check(ArchitectureModel, Codebase.CodebaseModel, Baselines.BaselineIndex, DiffContext?)" />;
///     a null <see cref="DiffContext" /> means no <c>--diff-base</c> was supplied and every tripwire skips.
///     Pure string logic only — no <c>Path.GetRelativePath</c>/Span (unavailable on netstandard2.0).
/// </summary>
public sealed class DiffContext
{
    private readonly HashSet<string> _changed;
    private readonly string _solutionPrefix;

    /// <summary>
    ///     Builds a diff context from the base ref, the solution directory, and the changed files
    ///     (any separators, any casing — all normalized to forward slashes).
    /// </summary>
    public DiffContext(string baseRef, string solutionDirectory, IEnumerable<string> changedFiles)
    {
        BaseRef = Guard.NotNull(baseRef, nameof(baseRef));
        SolutionDirectory = Normalize(Guard.NotNull(solutionDirectory, nameof(solutionDirectory))).TrimEnd('/');
        _solutionPrefix = SolutionDirectory + "/";
        _changed = new HashSet<string>(PathComparison.Comparer);
        foreach (string file in Guard.NotNull(changedFiles, nameof(changedFiles))) _changed.Add(Normalize(file));
        ChangedFiles = _changed;
    }

    /// <summary>The git ref the diff was taken against (echoed into <c>check --json</c>).</summary>
    public string BaseRef { get; }

    /// <summary>The solution directory, normalized to forward slashes with no trailing slash.</summary>
    public string SolutionDirectory { get; }

    /// <summary>The changed files, normalized to forward slashes; membership follows <see cref="PathComparison" />.</summary>
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