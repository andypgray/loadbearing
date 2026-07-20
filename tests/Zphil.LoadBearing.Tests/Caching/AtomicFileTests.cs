using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Tests.Caching;

/// <summary>
///     <see cref="AtomicFile" />: the committed-file atomic writer the baseline store and the managed-block
///     render adapter now write through. Proves the observable contract over scratch temp dirs — a
///     successful write leaves exactly the target (the temp sibling is renamed onto it, never left behind),
///     an existing target is overwritten in place, and a missing target directory is created first.
/// </summary>
public sealed class AtomicFileTests
{
    [Fact]
    public void WriteAllBytes_SuccessfulWrite_LeavesOnlyTheTargetNoTempSibling()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "target.bin");

            AtomicFile.WriteAllBytes(path, [1, 2, 3]);

            File.ReadAllBytes(path).ShouldBe([1, 2, 3]);
            // The temp sibling is moved onto the target, never abandoned: the directory holds exactly it.
            Directory.GetFiles(dir).Select(Path.GetFileName).ShouldBe(["target.bin"]);
        });
    }

    [Fact]
    public void WriteAllBytes_ExistingTarget_OverwritesInPlaceWithNoLeftoverTemp()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "target.bin");
            File.WriteAllBytes(path, [9, 9, 9, 9]);

            AtomicFile.WriteAllBytes(path, [1, 2, 3]);

            File.ReadAllBytes(path).ShouldBe([1, 2, 3]);
            Directory.GetFiles(dir).Select(Path.GetFileName).ShouldBe(["target.bin"]);
        });
    }

    [Fact]
    public void WriteAllBytes_MissingDirectory_CreatesItThenWrites()
    {
        WithTempDir(dir =>
        {
            string path = Path.Combine(dir, "nested", "deeper", "target.bin");

            AtomicFile.WriteAllBytes(path, [1, 2, 3]);

            File.ReadAllBytes(path).ShouldBe([1, 2, 3]);
        });
    }

    [Fact]
    public void WriteAllBytes_TargetPathIsExistingDirectory_ThrowsAndCleansUpTempSibling()
    {
        WithTempDir(dir =>
        {
            // The target path names an existing *directory*: the temp sibling writes fine, but the overwriting
            // File.Move cannot replace a directory with a file, so it throws — walking the catch's
            // TryDelete-then-rethrow leg. The failure must surface (not be swallowed to a bool, unlike the
            // cache stores that wrap this), and the scratch temp file must be deleted, not abandoned.
            string target = Path.Combine(dir, "occupied");
            Directory.CreateDirectory(target);

            Should.Throw<Exception>(() => AtomicFile.WriteAllBytes(target, [1, 2, 3]));

            // TryDelete ran on the failure path: the directory holds only the 'occupied' subdirectory — no
            // leftover "<target>.<guid>.tmp" sibling file.
            Directory.GetFiles(dir).ShouldBeEmpty();
        });
    }

    [Fact]
    public void Copy_SuccessfulCopy_LeavesOnlyTheTargetNoTempSibling()
    {
        WithTempDir(dir =>
        {
            string source = Path.Combine(dir, "source.bin");
            File.WriteAllBytes(source, [1, 2, 3]);
            string path = Path.Combine(dir, "target.bin");

            AtomicFile.Copy(source, path);

            File.ReadAllBytes(path).ShouldBe([1, 2, 3]);
            // The temp sibling is moved onto the target, never abandoned: beside the source, the directory holds
            // exactly it — no leftover "<target>.<guid>.tmp".
            Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(name => name).ShouldBe(["source.bin", "target.bin"]);
        });
    }

    [Fact]
    public void Copy_ExistingTarget_OverwritesInPlaceWithNoLeftoverTemp()
    {
        WithTempDir(dir =>
        {
            string source = Path.Combine(dir, "source.bin");
            File.WriteAllBytes(source, [1, 2, 3]);
            string path = Path.Combine(dir, "target.bin");
            File.WriteAllBytes(path, [9, 9, 9, 9]);

            AtomicFile.Copy(source, path);

            File.ReadAllBytes(path).ShouldBe([1, 2, 3]);
            Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(name => name).ShouldBe(["source.bin", "target.bin"]);
        });
    }

    [Fact]
    public void Copy_MissingDirectory_CreatesItThenCopies()
    {
        WithTempDir(dir =>
        {
            string source = Path.Combine(dir, "source.bin");
            File.WriteAllBytes(source, [1, 2, 3]);
            string path = Path.Combine(dir, "nested", "deeper", "target.bin");

            AtomicFile.Copy(source, path);

            File.ReadAllBytes(path).ShouldBe([1, 2, 3]);
        });
    }

    [Fact]
    public void Copy_DestinationPathIsExistingDirectory_ThrowsAndCleansUpTempSibling()
    {
        WithTempDir(dir =>
        {
            // The destination path names an existing *directory*: the temp sibling copies fine, but the
            // overwriting File.Move cannot replace a directory with a file, so it throws — walking the catch's
            // TryDelete-then-rethrow leg. The failure must surface (not be swallowed to a bool, unlike the cache
            // stores that wrap this), and the scratch temp file must be deleted, not abandoned.
            string source = Path.Combine(dir, "source.bin");
            File.WriteAllBytes(source, [1, 2, 3]);
            string target = Path.Combine(dir, "occupied");
            Directory.CreateDirectory(target);

            Should.Throw<Exception>(() => AtomicFile.Copy(source, target));

            // TryDelete ran on the failure path: only the source file and the 'occupied' subdirectory remain —
            // no leftover "<target>.<guid>.tmp" sibling.
            Directory.GetFiles(dir).Select(Path.GetFileName).ShouldBe(["source.bin"]);
        });
    }

    private static void WithTempDir(Action<string> body)
    {
        DirectoryInfo dir = Directory.CreateTempSubdirectory("lb-atomic-");
        try
        {
            body(dir.FullName);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}