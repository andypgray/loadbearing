using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     The walk's decisions, one row each. None of them is recoverable from the callers: all three copied
///     what they needed before the consolidation and would go on doing so, so nothing but these rows reds if
///     a later edit reaches for <see cref="SearchOption.AllDirectories" /> again, drops the overwrite, or
///     starts mirroring directories the predicate emptied.
/// </summary>
public sealed class DirectoryTreeTests
{
    [Fact]
    public void Copy_WithNoPredicate_TakesTheWholeTree()
    {
        using TempDirectory temp = TestTempRoot.Fresh("directory-tree");
        temp.WriteFile(["source", "top.txt"], "top");
        temp.WriteFile(["source", "nested", "deep", "leaf.txt"], "leaf");
        string destination = temp.PathOf("copy");

        DirectoryTree.Copy(temp.PathOf("source"), destination);

        RelativeFiles(destination)
            .ShouldBe(["nested/deep/leaf.txt", "top.txt"]);
        File.ReadAllText(Path.Combine(destination, "nested", "deep", "leaf.txt"))
            .ShouldBe("leaf");
    }

    [Fact]
    public void Copy_WithAPredicate_WithholdsByRelativePathAndLeavesNoEmptyDirectory()
    {
        using TempDirectory temp = TestTempRoot.Fresh("directory-tree");
        temp.WriteFile(["source", "keep.txt"], "top");
        temp.WriteFile(["source", "sub", "keep.txt"], "nested");
        string destination = temp.PathOf("copy");

        DirectoryTree.Copy(temp.PathOf("source"), destination, relative => relative != "sub/keep.txt");

        // The predicate sees the path, not the name: the same file name survives at the root. Its spelling is
        // the pinned part — forward separators and no leading one, whatever the platform's are.
        RelativeFiles(destination)
            .ShouldBe(["keep.txt"]);
        Directory.Exists(Path.Combine(destination, "sub"))
            .ShouldBeFalse("a directory the predicate emptied should not be mirrored into the copy");
    }

    [Fact]
    public void Copy_ADirectoryLink_ReproducesItRatherThanDescendingIt()
    {
        using TempDirectory temp = TestTempRoot.Fresh("directory-tree");
        temp.WriteFile(["source", "top.txt"], "top");
        temp.WriteFile(["outside", "beyond.txt"], "beyond");
        string outside = temp.PathOf("outside");
        SymlinkSupport.CreateDirectorySymlink(temp.PathOf("source", "link"), outside);
        string destination = temp.PathOf("copy");

        DirectoryTree.Copy(temp.PathOf("source"), destination);

        // Both wrong answers fail here: a walk that descended leaves a real directory of copied files, and
        // one that skipped the link leaves nothing — and neither has a LinkTarget.
        new DirectoryInfo(Path.Combine(destination, "link")).LinkTarget
            .ShouldBe(outside);
        Directory.EnumerateFiles(destination)
            .Select(Path.GetFileName)
            .ShouldBe(["top.txt"]);
    }

    [Fact]
    public void Copy_OverAFileAlreadyThere_OverwritesIt()
    {
        using TempDirectory temp = TestTempRoot.Fresh("directory-tree");
        temp.WriteFile(["source", "nested", "leaf.txt"], "new");
        temp.WriteFile(["copy", "nested", "leaf.txt"], "old");

        DirectoryTree.Copy(temp.PathOf("source"), temp.PathOf("copy"));

        File.ReadAllText(temp.PathOf("copy", "nested", "leaf.txt"))
            .ShouldBe("new");
    }

    private static string[] RelativeFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Select(relative => relative.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
