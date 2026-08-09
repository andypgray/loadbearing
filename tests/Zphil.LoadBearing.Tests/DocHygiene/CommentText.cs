using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Reduces C# source to the prose a maintainer wrote in it: every character outside comment trivia
///     is replaced with a blank, and comment text is left exactly where it sat. The result is the same
///     length as its input with newlines in their original positions, so a checker run over it reports
///     source line numbers without any offset arithmetic.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why mask rather than extract.</b> A run of consecutive <c>///</c> lines is one trivia
///         spanning many lines, so pulling each comment out and checking it would need its start line
///         plus a per-line offset inside it. Keeping the text in place makes the line number fall out
///         of the position, and lets the same checker serve this gate and the markdown gates.
///     </para>
///     <para>
///         <b>Why the blank is NUL and not a space.</b> U+0000 is neither a word character nor
///         whitespace to .NET's regex engine, so masked-out code can satisfy no part of any pattern —
///         not a <c>\s</c>, not a <c>\b</c>, not a literal. A space-filled gap looks like prose, and
///         a pattern that spans whitespace could report a match that exists in no comment. The blank
///         being inert is the property, and it holds for any pattern a future catalog adds rather
///         than for the ones written today.
///     </para>
///     <para>
///         <b>What this cannot see.</b> Prose inside a disabled <c>#if</c> arm lexes as
///         <c>DisabledTextTrivia</c>, and a <c>#region</c> name lives on a directive rather than in a
///         comment; neither is collected here. This repository contains none of either, and a reader
///         should know the boundary rather than assume the mask covers everything a person can type.
///     </para>
/// </remarks>
internal static class CommentText
{
    /// <summary>The inert character every non-comment position is replaced with.</summary>
    internal const char Blank = '\0';

    /// <summary>
    ///     Returns <paramref name="source" /> with everything outside comment trivia replaced by
    ///     <see cref="Blank" />, preserving length, and preserving <c>'\n'</c> and <c>'\r'</c> wherever
    ///     they occur so line numbering survives.
    /// </summary>
    public static string Mask(string source)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(source)
            .GetRoot();

        var masked = new char[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            char character = source[index];
            masked[index] = character is '\n' or '\r' ? character : Blank;
        }

        foreach (SyntaxTrivia trivia in root.DescendantTrivia())
        {
            if (!IsComment(trivia)) continue;

            TextSpan span = trivia.FullSpan;
            for (int index = span.Start; index < span.End; index++) masked[index] = source[index];
        }

        return new string(masked);
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.Kind() is SyntaxKind.SingleLineCommentTrivia
            or SyntaxKind.MultiLineCommentTrivia
            or SyntaxKind.SingleLineDocumentationCommentTrivia
            or SyntaxKind.MultiLineDocumentationCommentTrivia;
    }
}
