namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Deletes that survive the read-only attribute, which the framework's own do not: both
///     <see cref="File.Delete" /> and <see cref="Directory.Delete(string,bool)" /> throw
///     <see cref="UnauthorizedAccessException" /> on a read-only file rather than clearing it.
/// </summary>
/// <remarks>
///     <para>
///         Git is why this exists: it writes every loose object read-only, so any tree a
///         <see cref="TempGitRepo" /> has lived in defeats a plain delete — and it defeated two of them at
///         once. <see cref="TempFixtureWorkspace" />'s reset gave up partway through pruning <c>.git/</c>
///         and handed every later caller a private copy plus a full restore, and
///         <see cref="TestTempRoot" />'s sweep could then never remove the roots that were left behind.
///         Stranded roots under <c>%TEMP%/loadbearing-tests/fixtures/</c> showed the signature exactly: a
///         <c>.git</c> directory holding nothing but its read-only <c>objects/</c>, dated weeks earlier.
///     </para>
///     <para>
///         The clean path is the framework call, unchanged; clearing attributes is what the failure costs,
///         not what every delete costs. A file that is locked rather than read-only still throws
///         <see cref="IOException" />, which is what callers who fall back to a private copy are watching for.
///     </para>
/// </remarks>
internal static class ReadOnlyTolerant
{
    /// <summary>Deletes one file, clearing the read-only attribute only if that is what stood in the way.</summary>
    internal static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (UnauthorizedAccessException)
        {
            var info = new FileInfo(path);
            if (info is { Exists: true, IsReadOnly: true }) info.IsReadOnly = false;
            File.Delete(path);
        }
    }

    /// <summary>
    ///     Deletes a directory and everything under it; a directory that is already absent is a no-op.
    /// </summary>
    internal static void DeleteTree(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            Directory.Delete(path, true);
        }
        catch (UnauthorizedAccessException)
        {
            DeleteTreeClearingAttributes(path);
        }
    }

    // Walked by hand rather than with SearchOption.AllDirectories, because that recursion follows directory
    // links and these trees contain them (the symlinked-root tripwire test leaves one beside its repo).
    // A link is deleted, never descended into.
    private static void DeleteTreeClearingAttributes(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory)) Delete(file);

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            var info = new DirectoryInfo(child);
            if (info.LinkTarget is not null)
                info.Delete();
            else
                DeleteTreeClearingAttributes(child);
        }

        Directory.Delete(directory);
    }
}
