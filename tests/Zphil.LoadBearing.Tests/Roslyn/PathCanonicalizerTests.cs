using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     Direct proof of the managed realpath (<see cref="PathCanonicalizer" />): a path reached through a
///     symlinked ancestor resolves to its real target; a plain path is unchanged; drive roots and
///     nonexistent paths never throw. The symlink case skips on hosts that cannot create one (Windows
///     without Developer Mode/elevation); Linux and macOS CI exercise it.
/// </summary>
public sealed class PathCanonicalizerTests : IDisposable
{
    // Canonical so the "unchanged" and "real target" expectations are exact even on macOS (/var symlink).
    private readonly string _root =
        PathCanonicalizer.Resolve(Directory.CreateTempSubdirectory("loadbearing-canon-").FullName);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
            // best-effort: a leftover symlink or handle survives to OS temp cleanup.
        }
    }

    [Fact]
    public void Resolve_PlainCanonicalPath_IsUnchanged()
    {
        string dir = Directory.CreateDirectory(Path.Combine(_root, "plain")).FullName;

        PathCanonicalizer.Resolve(dir).ShouldBe(dir);
    }

    [Fact]
    public void Resolve_ThroughSymlinkedAncestor_ReturnsRealTarget()
    {
        string real = Directory.CreateDirectory(Path.Combine(_root, "real")).FullName;
        string sub = Directory.CreateDirectory(Path.Combine(real, "sub")).FullName;
        string file = Path.Combine(sub, "solution.slnx");
        File.WriteAllText(file, "");

        string link = Path.Combine(_root, "link");
        SymlinkSupport.CreateDirectorySymlink(link, real);

        // Reached through the symlinked ancestor 'link' → resolves to the real 'real/sub/solution.slnx'.
        PathCanonicalizer.Resolve(Path.Combine(link, "sub", "solution.slnx")).ShouldBe(file);
    }

    [Fact]
    public void Resolve_DriveRootOrRootDirectory_DoesNotThrow()
    {
        string root = Path.GetPathRoot(_root)!;

        Should.NotThrow(() => PathCanonicalizer.Resolve(root));
    }

    [Fact]
    public void Resolve_NonexistentPath_FallsBackToGetFullPath()
    {
        string nonexistent = Path.Combine(_root, "does-not-exist", "Ghost.slnx");

        PathCanonicalizer.Resolve(nonexistent).ShouldBe(Path.GetFullPath(nonexistent));
    }
}