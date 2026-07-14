using Shouldly;
using Xunit;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     Pins the hand-rolled netstandard2.0 <see cref="PathFormat.Relative" /> equivalent to the BCL's
///     <c>Path.GetRelativePath(dir, file)</c> (forward-slashed) it stands in for — Core cannot call
///     <c>GetRelativePath</c> (net5+ only), so this equivalence theory is what proves the hoist is
///     byte-neutral against the golden JSON and human acceptance surfaces. The oracle and the vectors are
///     both OS-shaped: <c>Path.GetRelativePath</c> only treats <c>\</c> as a separator and drive letters
///     as roots on Windows, so the Windows drive-letter vectors are pinned per-OS. CI runs on all three
///     platforms, hence the split — the Windows theory skips off Windows, and a POSIX theory carries the
///     rooted-path vectors that are only absolute there.
/// </summary>
public sealed class PathFormatTests
{
    [Theory]
    [InlineData(@"C:\sln", @"C:\sln\Web\Home.cs")] // nested
    [InlineData(@"C:\sln", @"C:\sln\a\b\c\Deep.cs")] // deeply nested
    [InlineData(@"C:\sln", @"C:\sln\Home.cs")] // file directly in dir
    [InlineData(@"C:\sln\Web", @"C:\sln\Domain\Order.cs")] // outside dir (..)
    [InlineData(@"C:\sln", @"D:\other\X.cs")] // different drive → fallback
    [InlineData(@"C:\Sln", @"c:\sln\Web\Home.cs")] // Windows casing difference
    [InlineData(@"C:\sln", "C:/sln/Web/Home.cs")] // mixed separators
    [InlineData(@"C:\sln\", @"C:\sln\Web\Home.cs")] // trailing separator on dir
    public void Relative_MatchesGetRelativePath(string solutionDirectory, string filePath)
    {
        // Off Windows, Path.GetRelativePath neither splits on '\' nor treats "C:\…" as rooted, so the
        // oracle and these drive-letter vectors are only meaningful on Windows.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows drive-letter path vectors.");
        string expected = Path.GetRelativePath(solutionDirectory, filePath).Replace('\\', '/');

        PathFormat.Relative(solutionDirectory, filePath).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/sln", "/sln/Web/Home.cs")] // nested
    [InlineData("/sln", "/sln/a/b/c/Deep.cs")] // deeply nested
    [InlineData("/sln", "/sln/Home.cs")] // file directly in dir
    [InlineData("/sln/Web", "/sln/Domain/Order.cs")] // outside dir (..)
    [InlineData("/sln", "/sln")] // target is the directory → "."
    [InlineData("/sln/", "/sln/Web/Home.cs")] // trailing separator on dir
    public void Relative_MatchesGetRelativePath_Posix(string solutionDirectory, string filePath)
    {
        // On Windows the leading-'/' vectors are drive-relative, not rooted, so GetRelativePath diverges;
        // these run on Linux and macOS, giving PathFormat.Relative its native-path coverage there.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX rooted-path vectors.");
        string expected = Path.GetRelativePath(solutionDirectory, filePath).Replace('\\', '/');

        PathFormat.Relative(solutionDirectory, filePath).ShouldBe(expected);
    }
}