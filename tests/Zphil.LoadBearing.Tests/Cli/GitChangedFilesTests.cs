using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Diff;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     <see cref="GitChangedFiles" /> pure parse/compose plumbing plus one real-process pin of the
///     loud-failure contract. The NUL parse tolerates a missing trailing separator; compose rebases
///     toplevel-relative paths onto absolute forward-slash paths and dedupes; running outside any repo
///     throws a <see cref="UserErrorException" /> (which the CLI maps to exit 2).
/// </summary>
[Collection("Serial")]
public sealed class GitChangedFilesTests
{
    // ComposeAbsolute runs Path.GetFullPath, which only leaves the toplevel untouched when it is already
    // rooted for the host OS — a Windows drive letter off Windows would get the CI working directory
    // prepended. Root the vector natively so the pins hold on every platform (production always gets a
    // real absolute toplevel from `git rev-parse --show-toplevel`).
    private static readonly string Toplevel = OperatingSystem.IsWindows() ? "C:/repo" : "/repo";

    [Fact]
    public void ParseZTerminated_EmptyOutput_IsEmpty()
    {
        GitChangedFiles.ParseZTerminated("").ShouldBeEmpty();
    }

    [Fact]
    public void ParseZTerminated_SplitsOnNulAndDropsTrailingEmpty()
    {
        GitChangedFiles.ParseZTerminated("a\0b\0").ShouldBe(["a", "b"]);
    }

    [Fact]
    public void ParseZTerminated_ToleratesMissingTrailingNul()
    {
        GitChangedFiles.ParseZTerminated("a\0b").ShouldBe(["a", "b"]);
    }

    [Fact]
    public void ComposeAbsolute_RebasesOntoToplevelWithForwardSlashes()
    {
        GitChangedFiles.ComposeAbsolute(Toplevel, ["App/Foo.cs", "App/Bar.cs"])
            .ShouldBe([$"{Toplevel}/App/Foo.cs", $"{Toplevel}/App/Bar.cs"]);
    }

    [Fact]
    public void ComposeAbsolute_DedupesRepeatedPaths()
    {
        GitChangedFiles.ComposeAbsolute(Toplevel, ["App/Foo.cs", "App/Foo.cs"])
            .ShouldBe([$"{Toplevel}/App/Foo.cs"]);
    }

    [Fact]
    public void Resolve_OutsideAnyRepo_ThrowsUserError()
    {
        string dir = Path.Combine(Path.GetTempPath(), "loadbearing-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Should.Throw<UserErrorException>(() => GitChangedFiles.Resolve("HEAD", dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}