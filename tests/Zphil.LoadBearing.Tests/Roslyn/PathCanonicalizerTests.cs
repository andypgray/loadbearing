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
    // Already canonical, so the "unchanged" and "real target" expectations are exact even on macOS (/var
    // symlink): TestTempRoot resolves the temp base once per run, and every path below hangs off that.
    private readonly TempDirectory _temp = TestTempRoot.Fresh("canon");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void Resolve_PlainCanonicalPath_IsUnchanged()
    {
        string dir = Directory.CreateDirectory(_temp.PathOf("plain"))
            .FullName;

        PathCanonicalizer.Resolve(dir)
            .ShouldBe(dir);
    }

    [Fact]
    public void Resolve_ThroughSymlinkedAncestor_ReturnsRealTarget()
    {
        string real = Directory.CreateDirectory(_temp.PathOf("real"))
            .FullName;
        string sub = Directory.CreateDirectory(Path.Combine(real, "sub"))
            .FullName;
        string file = Path.Combine(sub, "solution.slnx");
        File.WriteAllText(file, "");

        string link = _temp.PathOf("link");
        SymlinkSupport.CreateDirectorySymlink(link, real);

        // Reached through the symlinked ancestor 'link' → resolves to the real 'real/sub/solution.slnx'.
        PathCanonicalizer.Resolve(Path.Combine(link, "sub", "solution.slnx"))
            .ShouldBe(file);
    }

    [Fact]
    public void Resolve_DriveRootOrRootDirectory_DoesNotThrow()
    {
        string root = Path.GetPathRoot(_temp.Path)!;

        Should.NotThrow(() => PathCanonicalizer.Resolve(root));
    }

    [Fact]
    public void Resolve_NonexistentPath_FallsBackToGetFullPath()
    {
        string nonexistent = _temp.PathOf("does-not-exist", "Ghost.slnx");

        PathCanonicalizer.Resolve(nonexistent)
            .ShouldBe(Path.GetFullPath(nonexistent));
    }
}
