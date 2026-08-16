using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Unit and negative tests for <see cref="CommentText" />. They pin what the mask keeps, prove that
///     a forbidden token written as a string literal survives no mask — which is what spares the
///     reference gates any C# exemption at all — pin the two properties the gate's line numbers and its
///     blank character rest on, and pin the second reduction that takes the directives back out without
///     moving a single line.
/// </summary>
public sealed class CommentTextTests
{
    private static readonly Regex PhaseLabel = new(@"\bPhase\s+[0-9]");

    [Fact]
    public void Mask_LineComment_KeepsCommentAndBlanksCode()
    {
        // Arrange
        var source = "var count = 1; // a trailing note";

        // Act
        string masked = CommentText.Mask(source);

        // Assert
        masked.ShouldBe(new string(CommentText.Blank, "var count = 1; ".Length) + "// a trailing note");
    }

    [Fact]
    public void Mask_BlockComment_KeptWithItsDelimiters()
    {
        // Arrange
        var source = "/* kept */ var count = 1;";

        // Act
        string masked = CommentText.Mask(source);

        // Assert
        masked.ShouldBe("/* kept */" + new string(CommentText.Blank, " var count = 1;".Length));
    }

    [Theory]
    [InlineData("""var label = "Phase 9";""")]
    [InlineData("""var label = @"Phase 9";""")]
    [InlineData("""var label = "// Phase 9";""")]
    [InlineData("""var label = "/* WP 3 */";""")]
    [InlineData("""[Trait("Phase 9", "WP 3")] void M() { }""")]
    public void Mask_ForbiddenTokenInsideStringLiteral_IsBlanked(string statement)
    {
        // Act
        string masked = CommentText.Mask(statement);

        // Assert: a literal is a token, never trivia, so the whole statement blanks away. This is what
        // spares the gate a C# exemption list — the discrimination is at the argument, not the file.
        masked.ShouldBe(new string(CommentText.Blank, statement.Length));
    }

    [Fact]
    public void Mask_RawStringLiteralHoldingCommentSyntax_IsBlanked()
    {
        // Arrange: text that reads as a comment to a person but lexes as a literal to the parser.
        const string source = """"
                              var label = """
                                          // Phase 9
                                          """;
                              """";

        // Act
        string masked = CommentText.Mask(source);

        // Assert
        masked.ShouldNotContain("Phase");
        DocProse.FindForbidden(masked, [PhaseLabel])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Mask_MultiLineDocComment_ReportsTheSourceLineOfTheHit()
    {
        // Arrange: a run of consecutive /// lines is one trivia, so a hit on its third line is the
        // case that would need offset arithmetic if comments were extracted rather than masked.
        string[] lines =
        [
            "namespace N;",
            "",
            "/// <summary>",
            "///     Written in Phase 9 of the build.",
            "/// </summary>",
            "public sealed class C;"
        ];
        string source = string.Join("\n", lines);

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(CommentText.Mask(source), [PhaseLabel]);

        // Assert
        hits.ShouldHaveSingleItem()
            .ShouldBe("4: Phase 9");
    }

    [Fact]
    public void Mask_CrlfSource_ReportsTheSourceLineOfTheHit()
    {
        // Arrange
        var source = "var a = 1;\r\n// Phase 9\r\nvar b = 2;";

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(CommentText.Mask(source), [PhaseLabel]);

        // Assert
        hits.ShouldHaveSingleItem()
            .ShouldBe("2: Phase 9");
    }

    [Fact]
    public void Mask_BlankedCodeCannotBridgeTwoComments()
    {
        // Arrange: two comments on one line with code between them. The checker reads a line at a
        // time, so this is the only shape in which a match could straddle two separate comments.
        var source = "/* the */ var spacer = 1; /* current phase */";

        // Act
        string masked = CommentText.Mask(source);

        // Assert
        masked.ShouldBe(source.Replace(" var spacer = 1; ", new string(CommentText.Blank, 17)));
        Regex.IsMatch(masked, @"\bthe\s+current\s+phase\b")
            .ShouldBeFalse();

        // And the reason it holds for every pattern rather than just this one: the blank satisfies
        // neither of the two classes a pattern can cross a gap with.
        Regex.IsMatch(CommentText.Blank.ToString(), @"\s")
            .ShouldBeFalse();
        Regex.IsMatch(CommentText.Blank.ToString(), @"\w")
            .ShouldBeFalse();
    }

    [Fact]
    public void Mask_AnySource_PreservesLengthAndNewlinePositions()
    {
        // Arrange
        var source = "using System;\r\n\n// note\nnamespace N;\n\npublic sealed class C;\n";

        // Act
        string masked = CommentText.Mask(source);

        // Assert
        masked.Length.ShouldBe(source.Length);
        for (var index = 0; index < source.Length; index++)
            if (source[index] is '\n' or '\r')
                masked[index]
                    .ShouldBe(source[index]);
    }

    [Fact]
    public void Mask_SourceThatDoesNotParse_StillReturnsItsComments()
    {
        // Arrange: the gate reads whatever git tracks, so a file mid-edit must not throw.
        var source = "// a note\npublic sealed class C { void M( }";

        // Act
        string masked = CommentText.Mask(source);

        // Assert
        masked.ShouldStartWith("// a note");
        masked.Length.ShouldBe(source.Length);
    }

    [Fact]
    public void WithoutSuppressionDirectives_SuppressionComment_IsBlankedToEndOfLine()
    {
        // Arrange
        var source = "// ReSharper disable once UseCollectionExpression\nvar x = 1;";

        // Act
        string stripped = CommentText.WithoutSuppressionDirectives(CommentText.Mask(source));

        // Assert
        string blankedDirective =
            new(CommentText.Blank, "// ReSharper disable once UseCollectionExpression".Length);
        string blankedCode = new(CommentText.Blank, "var x = 1;".Length);
        stripped.ShouldBe(blankedDirective + "\n" + blankedCode);
    }

    [Theory]
    [InlineData("// ReSharper disable once UseCollectionExpression", "ReSharper")]
    [InlineData("// ReSharper disable All", "ReSharper")]
    [InlineData("    // ReSharper restore StaticMemberInGenericType", "ReSharper")]
    [InlineData("// @formatter:off", "@formatter")]
    [InlineData("// @formatter:on", "@formatter")]
    public void WithoutSuppressionDirectives_EveryDirectiveForm_LosesItsToken(string directive, string token)
    {
        // Act
        string stripped = CommentText.WithoutSuppressionDirectives(CommentText.Mask(directive));

        // Assert
        stripped.ShouldNotContain(token);
        stripped.Length.ShouldBe(directive.Length);
    }

    [Fact]
    public void WithoutSuppressionDirectives_ProseThatMerelyNamesTheTool_Survives()
    {
        // Arrange: the shape the source gate's exemption list exists for — a doc comment saying why
        // the suppression under it is load-bearing, which is prose rather than a directive.
        var source = "/// <summary>The disables below hold against ReSharper.</summary>\npublic sealed class C;";

        // Act
        string stripped = CommentText.WithoutSuppressionDirectives(CommentText.Mask(source));

        // Assert
        stripped.ShouldContain("hold against ReSharper");
    }

    [Fact]
    public void WithoutSuppressionDirectives_HitBelowADirective_KeepsItsSourceLine()
    {
        // Arrange: blanking in place rather than dropping the line is the whole property — a line
        // removed here would report every later hit one line early.
        string[] lines =
        [
            "// ReSharper disable once UnusedMember.Local",
            "internal static void M() { }",
            "",
            "// Written in Phase 9 of the build."
        ];
        string source = string.Join("\n", lines);

        // Act
        string stripped = CommentText.WithoutSuppressionDirectives(CommentText.Mask(source));

        // Assert
        DocProse.FindForbidden(stripped, [PhaseLabel])
            .ShouldHaveSingleItem()
            .ShouldBe("4: Phase 9");
    }

    [Fact]
    public void WithoutSuppressionDirectives_AnySource_PreservesLengthAndNewlinePositions()
    {
        // Arrange
        var source = "// ReSharper disable once Foo\r\nvar a = 1;\n// @formatter:off\r\nvar b = 2;\n";

        // Act
        string stripped = CommentText.WithoutSuppressionDirectives(CommentText.Mask(source));

        // Assert
        stripped.Length.ShouldBe(source.Length);
        for (var index = 0; index < source.Length; index++)
            if (source[index] is '\n' or '\r')
                stripped[index]
                    .ShouldBe(source[index]);
    }
}
