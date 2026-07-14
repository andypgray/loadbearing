using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     <see cref="DiffContext" /> path plumbing (GRAMMAR §7): forward-slash normalization,
///     separator- and case-insensitive membership, and solution-relative rendering — pure string
///     logic (no <c>Path.GetRelativePath</c>, netstandard2.0 discipline).
/// </summary>
public sealed class DiffContextTests
{
    private static DiffContext Make(params string[] changed)
    {
        return new DiffContext("HEAD", @"C:\repo\sln", changed);
    }

    [Fact]
    public void ChangedFiles_AreNormalizedToForwardSlashes()
    {
        Make(@"C:\repo\sln\App\Foo.cs").ChangedFiles.ShouldBe(["C:/repo/sln/App/Foo.cs"]);
    }

    [Fact]
    public void Contains_MatchesRegardlessOfSeparatorAndCase()
    {
        DiffContext diff = Make(@"C:\repo\sln\App\Foo.cs");

        diff.Contains(@"C:\repo\sln\App\Foo.cs").ShouldBeTrue();
        diff.Contains("C:/repo/sln/App/Foo.cs").ShouldBeTrue();
        diff.Contains("c:/REPO/sln/app/foo.cs").ShouldBeTrue();
    }

    [Fact]
    public void Contains_UnchangedFile_IsFalse()
    {
        Make(@"C:\repo\sln\App\Foo.cs").Contains(@"C:\repo\sln\App\Bar.cs").ShouldBeFalse();
    }

    [Fact]
    public void SolutionRelative_StripsTheSolutionPrefix()
    {
        Make().SolutionRelative(@"C:\repo\sln\App\Foo.cs").ShouldBe("App/Foo.cs");
    }

    [Fact]
    public void SolutionRelative_OutsideTheSolution_ReturnsNormalizedPathUnchanged()
    {
        Make().SolutionRelative(@"D:\other\Baz.cs").ShouldBe("D:/other/Baz.cs");
    }

    [Fact]
    public void SolutionDirectory_IsNormalizedForwardSlashWithNoTrailingSlash()
    {
        new DiffContext("HEAD", @"C:\repo\sln\", []).SolutionDirectory.ShouldBe("C:/repo/sln");
    }
}