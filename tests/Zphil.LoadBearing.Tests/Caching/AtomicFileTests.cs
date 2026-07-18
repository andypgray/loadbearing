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