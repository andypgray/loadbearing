using Shouldly;
using Xunit;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Tests.Prose;

/// <summary>
///     The GRAMMAR §6 reference-list join, pinned directly on <see cref="ProseFormat.JoinReferences" />:
///     the empty (<c>Prose/ProseFormat.cs:76</c>), two, and three-plus forms — the last proving the
///     no-Oxford-comma rule.
/// </summary>
public sealed class ProseFormatTests
{
    [Fact]
    public void JoinReferences_EmptyList_ReturnsEmptyString()
    {
        ProseFormat.JoinReferences([]).ShouldBe(string.Empty);
    }

    [Fact]
    public void JoinReferences_TwoReferences_JoinsWithOr()
    {
        ProseFormat.JoinReferences(["`A`", "`B`"]).ShouldBe("`A` or `B`");
    }

    [Fact]
    public void JoinReferences_ThreeReferences_UsesNoOxfordComma()
    {
        ProseFormat.JoinReferences(["`A`", "`B`", "`C`"]).ShouldBe("`A`, `B` or `C`");
    }
}