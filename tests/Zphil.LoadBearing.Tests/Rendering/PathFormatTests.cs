using Shouldly;
using Xunit;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     Pins the hand-rolled netstandard2.0 <see cref="PathFormat.Relative" /> equivalent to the BCL's
///     <c>Path.GetRelativePath(dir, file)</c> (forward-slashed) it stands in for — Core cannot call
///     <c>GetRelativePath</c> (net5+ only), so this equivalence theory is what proves the hoist is
///     byte-neutral against the golden JSON and human acceptance surfaces, per-OS (these run on Windows,
///     the maintainer host).
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
        string expected = Path.GetRelativePath(solutionDirectory, filePath).Replace('\\', '/');

        PathFormat.Relative(solutionDirectory, filePath).ShouldBe(expected);
    }
}