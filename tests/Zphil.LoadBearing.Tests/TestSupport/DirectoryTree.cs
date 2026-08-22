namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The test project's one recursive directory copy. A fixture's private copy, a spec's staged output
///     and a staged MCP server directory all take this walk, differing only in which files they keep.
/// </summary>
/// <remarks>
///     <para>
///         Three near-identical walks stood here before, each having separately decided the same three
///         questions. Deciding them once, out loud, is the point of there being one:
///     </para>
///     <para>
///         <b>A directory link is copied, never descended.</b> The recursion is by hand rather than
///         <see cref="SearchOption.AllDirectories" />, whose enumeration walks into link targets, and these
///         trees contain links — the symlinked-root tripwire test leaves one beside its repo.
///         <see cref="ReadOnlyTolerant" />'s delete already picked that side and says why; a copy that
///         descended where the delete will not would build trees nothing can take down again.
///     </para>
///     <para>
///         <b>Files overwrite.</b> The union of the three: the fixture copy already passed
///         <c>overwrite: true</c>, and the other two only survived without it because their callers cleared
///         the destination first — a property of those callers, not of the walk.
///     </para>
///     <para>
///         <b>Destination directories are created per copied file</b>, so a source directory whose every
///         file <c>include</c> withholds leaves no empty directory behind. The two withholding walks used to
///         reproduce every source directory whatever survived in it. Nothing reads those — git does not
///         track empty directories, and assembly resolution does not enumerate them — but the result is not
///         a directory-for-directory mirror and should not be read as one.
///     </para>
/// </remarks>
internal static class DirectoryTree
{
    /// <summary>
    ///     Copies <paramref name="source" /> into <paramref name="destination" />, keeping every file
    ///     <paramref name="include" /> accepts. The predicate receives the file's path relative to
    ///     <paramref name="source" />, separators normalized to <c>/</c> and no leading one; a null
    ///     predicate keeps everything.
    /// </summary>
    internal static void Copy(string source, string destination, Func<string, bool>? include = null)
    {
        Directory.CreateDirectory(destination);
        CopyInto(source, destination, string.Empty, include);
    }

    private static void CopyInto(
        string source, string destination, string relativePrefix, Func<string, bool>? include)
    {
        foreach (string file in Directory.EnumerateFiles(source))
        {
            string name = Path.GetFileName(file);
            if (include is not null && !include(relativePrefix + name)) continue;

            Directory.CreateDirectory(destination);
            File.Copy(file, Path.Combine(destination, name), true);
        }

        foreach (string child in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(child);
            string childDestination = Path.Combine(destination, name);
            string? linkTarget = new DirectoryInfo(child).LinkTarget;

            if (linkTarget is null)
            {
                CopyInto(child, childDestination, relativePrefix + name + "/", include);
                continue;
            }

            Directory.CreateDirectory(destination);
            Directory.CreateSymbolicLink(childDestination, linkTarget);
        }
    }
}
