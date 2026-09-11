using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Tests.Caching;

/// <summary>
///     Pins what a document's shape is blind to and what it is not: line moves, indentation and comments
///     leave the hash alone, and everything a fact could be read from — identifiers, literals, directives,
///     disabled text, and the generated-source banner's verdict — changes it. Then pins the line map that
///     equal hashes buy: shifted lines map, joined lines share an entry, a split refuses to map at all.
/// </summary>
public sealed class SourceShapeTests
{
    // A small class with the four things a document usually has: a using, a namespace, a field, and two
    // methods. Deliberately without blank lines, so the cases that insert one control the whole difference.
    private const string Widget = """
                                  using System;
                                  namespace Shop;
                                  public sealed class Widget
                                  {
                                      private readonly int _size = 3;
                                      public int Grow(int by)
                                      {
                                          return _size + by;
                                      }
                                      public string Label()
                                      {
                                          return "widget";
                                      }
                                  }
                                  """;

    // A method whose whole body sits on its declaration line — the shape a split rewrites.
    private const string OneLineBody = """
                                       namespace Shop;
                                       public sealed class Gadget
                                       {
                                           public void Ping() { }
                                       }
                                       """;

    private const string SplitBody = """
                                     namespace Shop;
                                     public sealed class Gadget
                                     {
                                         public void Ping()
                                         { }
                                     }
                                     """;

    private const string TwoStatements = """
                                         namespace Shop;
                                         public sealed class Gadget
                                         {
                                             public int Sum(int a, int b)
                                             {
                                                 int total = a;
                                                 total += b;
                                                 return total;
                                             }
                                         }
                                         """;

    private const string JoinedStatements = """
                                            namespace Shop;
                                            public sealed class Gadget
                                            {
                                                public int Sum(int a, int b)
                                                {
                                                    int total = a; total += b;
                                                    return total;
                                                }
                                            }
                                            """;

    private const string DisabledBlock = """
                                         #if false
                                         public class Ghost { int hidden = 1; }
                                         #endif
                                         namespace Shop;
                                         public sealed class Gadget
                                         {
                                         }
                                         """;

    // The comment pair every shifting case inserts above the class, so the shift is always the same two lines.
    private const string CommentedClass = """
                                          // a note nothing extracts
                                          /// <summary>A widget.</summary>
                                          public sealed class Widget
                                          """;

    [Fact]
    public void Of_CommentLinesInsertedBeforeTheClass_KeepsTheHashAndShiftsTheMap()
    {
        // Arrange — an ordinary comment and a doc comment above the class, the edit this whole feature is for.
        string commented = WithCommentsAboveTheClass();

        // Act
        SourceShape before = ShapeOf(Widget);
        SourceShape after = ShapeOf(commented);

        // Assert
        after.Hash.ShouldBe(before.Hash);
        after.TokensPerLine.Count.ShouldBe(before.TokensPerLine.Count + 2);

        LineMap map = SourceShape.TryMapLines(before, after)
            .ShouldNotBeNull();
        map.TryMap(3, out int classLine)
            .ShouldBeTrue();
        classLine.ShouldBe(5);
        map.IsIdentity.ShouldBeFalse();
    }

    [Fact]
    public void Of_ReindentedWithoutMovingTokens_KeepsTheHashAndMapsToItself()
    {
        // Arrange — two spaces added at each end of every non-empty line: every token stays on its line.
        string reindented = string.Join("\n", Widget.Split('\n')
            .Select(line => line.Length == 0 ? line : "  " + line + "  "));

        // Act
        SourceShape before = ShapeOf(Widget);
        SourceShape after = ShapeOf(reindented);

        // Assert
        after.Hash.ShouldBe(before.Hash);

        LineMap map = SourceShape.TryMapLines(before, after)
            .ShouldNotBeNull();
        map.IsIdentity.ShouldBeTrue();
    }

    [Fact]
    public void Of_BlankLineInserted_KeepsTheHashAndShiftsTheMap()
    {
        // Arrange
        string spaced = Widget.Replace("namespace Shop;", "\nnamespace Shop;", StringComparison.Ordinal);

        // Act
        SourceShape before = ShapeOf(Widget);
        SourceShape after = ShapeOf(spaced);

        // Assert
        after.Hash.ShouldBe(before.Hash);

        LineMap map = SourceShape.TryMapLines(before, after)
            .ShouldNotBeNull();
        map.IsIdentity.ShouldBeFalse();
        map.TryMap(2, out int namespaceLine)
            .ShouldBeTrue();
        namespaceLine.ShouldBe(3);
        map.TryMap(3, out int classLine)
            .ShouldBeTrue();
        classLine.ShouldBe(4);
    }

    [Fact]
    public void Of_CrlfInsteadOfLf_KeepsTheHashAndTheLineProfile()
    {
        // Arrange
        string crlf = string.Join("\r\n", Widget.Split('\n'));

        // Act
        SourceShape lineFeeds = ShapeOf(Widget);
        SourceShape carriageReturns = ShapeOf(crlf);

        // Assert
        carriageReturns.Hash.ShouldBe(lineFeeds.Hash);
        carriageReturns.TokensPerLine.ShouldBe(lineFeeds.TokensPerLine);
    }

    [Fact]
    public void Of_IdentifierRenamed_ChangesTheHash()
    {
        // Arrange
        string renamed = Widget.Replace("Grow", "Expand", StringComparison.Ordinal);

        // Act
        SourceShape before = ShapeOf(Widget);
        SourceShape after = ShapeOf(renamed);

        // Assert
        after.Hash.ShouldNotBe(before.Hash);
    }

    [Fact]
    public void Of_StringLiteralEdited_ChangesTheHash()
    {
        // Arrange
        string edited = Widget.Replace("\"widget\"", "\"gadget\"", StringComparison.Ordinal);

        // Act
        SourceShape before = ShapeOf(Widget);
        SourceShape after = ShapeOf(edited);

        // Assert
        after.Hash.ShouldNotBe(before.Hash);
    }

    [Fact]
    public void Of_NumericLiteralEdited_ChangesTheHash()
    {
        // Arrange
        string edited = Widget.Replace("_size = 3;", "_size = 4;", StringComparison.Ordinal);

        // Act
        SourceShape before = ShapeOf(Widget);
        SourceShape after = ShapeOf(edited);

        // Assert
        after.Hash.ShouldNotBe(before.Hash);
    }

    [Fact]
    public void Of_PragmaDirectiveInserted_ChangesTheHash()
    {
        // Arrange — the directive cannot move a fact here, and is significant anyway: the shape is
        // deliberately conservative about everything but whitespace and comments.
        string directed = "#pragma warning disable CS0414" + "\n" + Widget;

        // Act
        SourceShape before = ShapeOf(Widget);
        SourceShape after = ShapeOf(directed);

        // Assert
        after.Hash.ShouldNotBe(before.Hash);
    }

    [Fact]
    public void Of_RegionDirectiveInserted_ChangesTheHash()
    {
        // Arrange
        string regioned = "#region Body" + "\n" + Widget + "\n" + "#endregion";

        // Act
        SourceShape before = ShapeOf(Widget);
        SourceShape after = ShapeOf(regioned);

        // Assert
        after.Hash.ShouldNotBe(before.Hash);
    }

    [Fact]
    public void Of_ClassWrappedInAnIfDirective_ChangesTheHash()
    {
        // Arrange
        string conditional = "#if DEBUG" + "\n" + Widget + "\n" + "#endif";

        // Act
        SourceShape before = ShapeOf(Widget);
        SourceShape after = ShapeOf(conditional);

        // Assert
        after.Hash.ShouldNotBe(before.Hash);
    }

    [Fact]
    public void Of_EditInsideDisabledText_ChangesTheHash()
    {
        // Arrange — the edit is in text no token comes from, which is exactly why it has to be hashed:
        // flip the condition later and those characters become facts.
        string edited = DisabledBlock.Replace("hidden = 1", "hidden = 2", StringComparison.Ordinal);

        // Act
        SourceShape before = ShapeOf(DisabledBlock);
        SourceShape after = ShapeOf(edited);

        // Assert
        after.Hash.ShouldNotBe(before.Hash);
    }

    [Fact]
    public void Of_GeneratedBannerLeadingTheFile_ChangesTheHash()
    {
        // Arrange — the comment's own text is not hashed, so the banner verdict is the only thing that can
        // carry this difference into the digest.
        string banner = "// <auto-generated />" + "\n" + OneLineBody;

        // Act
        SourceShape before = ShapeOf(OneLineBody);
        SourceShape after = ShapeOf(banner);

        // Assert
        after.Hash.ShouldNotBe(before.Hash);
    }

    [Fact]
    public void Of_TheSameWordsInACommentBelowTheFirstToken_KeepsTheHash()
    {
        // Arrange — below the first token the words are prose about the convention, not a claim under it.
        string mention = OneLineBody.Replace(
            "    public void Ping()",
            "    // <auto-generated />\n    public void Ping()",
            StringComparison.Ordinal);

        // Act
        SourceShape before = ShapeOf(OneLineBody);
        SourceShape after = ShapeOf(mention);

        // Assert
        after.Hash.ShouldBe(before.Hash);
    }

    [Fact]
    public void TryMapLines_OneLineSplitAcrossTwo_ReturnsNull()
    {
        // Arrange
        SourceShape before = ShapeOf(OneLineBody);
        SourceShape after = ShapeOf(SplitBody);

        // Act
        LineMap? map = SourceShape.TryMapLines(before, after);

        // Assert — the texts are shape-equal, so the split is the only thing refusing the map.
        after.Hash.ShouldBe(before.Hash);
        map.ShouldBeNull();
    }

    [Fact]
    public void TryMapLines_TwoLinesJoinedOntoOne_MapsBothToIt()
    {
        // Arrange
        SourceShape before = ShapeOf(TwoStatements);
        SourceShape after = ShapeOf(JoinedStatements);

        // Act
        LineMap map = SourceShape.TryMapLines(before, after)
            .ShouldNotBeNull();

        // Assert
        map.TryMap(6, out int first)
            .ShouldBeTrue();
        first.ShouldBe(6);
        map.TryMap(7, out int second)
            .ShouldBeTrue();
        second.ShouldBe(6);
        map.TryMap(8, out int following)
            .ShouldBeTrue();
        following.ShouldBe(7);
    }

    [Fact]
    public void TryMapLines_LineCarryingNoTokenStart_HasNoEntry()
    {
        // Arrange
        SourceShape shape = ShapeOf(WithCommentsAboveTheClass());

        // Act
        LineMap map = SourceShape.TryMapLines(shape, shape)
            .ShouldNotBeNull();

        // Assert — neither comment line starts a token, so neither is a line a site could sit on.
        map.TryMap(3, out _)
            .ShouldBeFalse();
        map.TryMap(4, out _)
            .ShouldBeFalse();
        map.TryMap(5, out int classLine)
            .ShouldBeTrue();
        classLine.ShouldBe(5);
    }

    [Fact]
    public void Equals_ShapesAndMapsOfTheSameText_CompareByValue()
    {
        // Arrange
        SourceShape commented = ShapeOf(WithCommentsAboveTheClass());

        // Act
        SourceShape first = ShapeOf(Widget);
        SourceShape second = ShapeOf(Widget);
        SourceShape other = ShapeOf(OneLineBody);
        LineMap firstMap = SourceShape.TryMapLines(first, commented)
            .ShouldNotBeNull();
        LineMap secondMap = SourceShape.TryMapLines(second, commented)
            .ShouldNotBeNull();

        // Assert — reference equality would make a persisted stamp look changed on every run.
        first.ShouldBe(second);
        (first == second).ShouldBeTrue();
        first.GetHashCode()
            .ShouldBe(second.GetHashCode());
        first.ShouldNotBe(other);

        firstMap.ShouldNotBeSameAs(secondMap);
        firstMap.ShouldBe(secondMap);
        firstMap.GetHashCode()
            .ShouldBe(secondMap.GetHashCode());
    }

    private static string WithCommentsAboveTheClass()
    {
        return Widget.Replace("public sealed class Widget", CommentedClass, StringComparison.Ordinal);
    }

    private static SourceShape ShapeOf(string text)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, path: "Widget.cs");
        return SourceShape.Of(tree);
    }
}
