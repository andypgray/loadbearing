using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Zphil.LoadBearing.Roslyn.Extraction;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     One document's facts up to line numbers: a digest over everything extraction reads apart from
///     <em>where</em> it reads it, plus the per-line profile that says where. Two texts of one document
///     whose <see cref="Hash" />es are equal produce identical extraction facts modulo line numbers, and
///     <see cref="TryMapLines" /> turns that into the old-line-to-new-line map a stored
///     <see cref="FragmentSite" /> can be moved through instead of re-walked.
/// </summary>
/// <remarks>
///     <para>
///         The digest is deliberately conservative: only whitespace, line endings and comments are left
///         out of it, so a directive, an edit inside disabled text, a skipped token or a conflict marker
///         re-walks the document even where it could not have moved a fact. What makes those exclusions
///         sound is that no extraction walker descends into trivia and exactly one fact reads a comment —
///         the generated-source banner, which enters the digest as
///         <see cref="GeneratedSourceSignals.HasGeneratedBanner">its verdict</see> rather than its text.
///     </para>
///     <para>
///         <see cref="TokensPerLine" /> counts token <em>starts</em> per 0-based line and excludes
///         zero-width tokens, which mint no site: counting the end-of-file token would read a newline
///         appended at the end of a file as a line whose tokens split. Their trivia is still hashed — the
///         <c>#endif</c> closing a file is leading trivia of that token. The line it counts on must stay
///         the one <see cref="FragmentSite.Of(SyntaxTree, TextSpan)" /> records, or a map moves sites onto
///         lines they were never on.
///     </para>
///     <para>
///         Equality is spelled out because a record compares a list member by reference: a caller deciding
///         whether to rewrite a persisted stamp by comparing shapes would otherwise rewrite it every run.
///     </para>
/// </remarks>
internal sealed record SourceShape(string Hash, IReadOnlyList<int> TokensPerLine)
{
    // The kind and the UTF-8 byte length that prefix every element fed to the digest.
    private const int HeaderBytes = 2 * sizeof(int);

    /// <summary>The shape of <paramref name="tree" />'s text as it stands.</summary>
    internal static SourceShape Of(SyntaxTree tree)
    {
        SyntaxNode root = tree.GetRoot();
        SourceText text = tree.GetText();
        var tokensPerLine = new int[text.Lines.Count];

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (SyntaxToken token in root.DescendantTokens())
        {
            Append(digest, token.RawKind, token.Text);
            AppendSignificantTrivia(digest, token.LeadingTrivia);
            AppendSignificantTrivia(digest, token.TrailingTrivia);

            if (token.Span.IsEmpty) continue;

            int line = tree.GetLineSpan(token.Span).StartLinePosition.Line;
            tokensPerLine[line]++;
        }

        AppendBannerVerdict(digest, tree);

        string hash = Convert.ToHexStringLower(digest.GetHashAndReset());
        return new SourceShape(hash, tokensPerLine);
    }

    /// <summary>
    ///     The map from <paramref name="before" />'s 1-based lines to <paramref name="after" />'s, or
    ///     <see langword="null" /> where there is none: the two are not shapes of one document, or some
    ///     line's tokens were split across two lines. Lines joined onto one line map together; a line
    ///     carrying no token start gets no entry.
    /// </summary>
    /// <remarks>
    ///     Equal hashes mean equal token sequences, so the k-th token of one text is the k-th token of the
    ///     other and the walk below can pair lines by running ordinal alone. The token-total guard restates
    ///     what equal hashes already imply, and is what makes the walk total rather than dependent on that
    ///     implication holding.
    /// </remarks>
    internal static LineMap? TryMapLines(SourceShape before, SourceShape after)
    {
        if (!string.Equals(before.Hash, after.Hash, StringComparison.Ordinal)) return null;
        if (TotalTokens(before) != TotalTokens(after)) return null;

        var newLineByOldLine = new int[before.TokensPerLine.Count];
        var newLine = 0;
        var consumed = 0;

        for (var oldLine = 0; oldLine < before.TokensPerLine.Count; oldLine++)
        {
            int tokens = before.TokensPerLine[oldLine];
            if (tokens == 0) continue;

            while (newLine < after.TokensPerLine.Count && consumed >= after.TokensPerLine[newLine])
            {
                newLine++;
                consumed = 0;
            }

            if (newLine == after.TokensPerLine.Count) return null;
            if (consumed + tokens > after.TokensPerLine[newLine]) return null;

            consumed += tokens;
            newLineByOldLine[oldLine] = newLine + 1;
        }

        return new LineMap(newLineByOldLine);
    }

    /// <inheritdoc />
    public bool Equals(SourceShape? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (!string.Equals(Hash, other.Hash, StringComparison.Ordinal)) return false;
        if (TokensPerLine.Count != other.TokensPerLine.Count) return false;

        for (var line = 0; line < TokensPerLine.Count; line++)
            if (TokensPerLine[line] != other.TokensPerLine[line])
                return false;

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var code = new HashCode();
        code.Add(Hash, StringComparer.Ordinal);
        code.Add(TokensPerLine.Count);
        foreach (int tokens in TokensPerLine) code.Add(tokens);

        return code.ToHashCode();
    }

    private static long TotalTokens(SourceShape shape)
    {
        long total = 0;
        foreach (int tokens in shape.TokensPerLine) total += tokens;

        return total;
    }

    private static void AppendSignificantTrivia(IncrementalHash digest, SyntaxTriviaList trivia)
    {
        foreach (SyntaxTrivia item in trivia)
        {
            if (IsIgnored(item)) continue;

            Append(digest, item.RawKind, item.ToFullString());
        }
    }

    // The trivia no fact reads. Everything else — directives, disabled text, skipped tokens, conflict
    // markers, the shebang — is significant, and the type's remarks say why that is deliberately more
    // than extraction can actually be moved by.
    private static bool IsIgnored(SyntaxTrivia trivia)
    {
        return trivia.Kind() switch
        {
            SyntaxKind.WhitespaceTrivia
                or SyntaxKind.EndOfLineTrivia
                or SyntaxKind.SingleLineCommentTrivia
                or SyntaxKind.MultiLineCommentTrivia
                or SyntaxKind.SingleLineDocumentationCommentTrivia
                or SyntaxKind.MultiLineDocumentationCommentTrivia => true,
            _ => false
        };
    }

    // One element: its kind, its UTF-8 byte length, then its bytes. Length-prefixing is what stops two
    // different element sequences producing one byte stream. The buffer is rented rather than allocated
    // because a document contributes an element per token and per significant trivia, and the shape is
    // recomputed per document per edit.
    private static void Append(IncrementalHash digest, int rawKind, string text)
    {
        int capacity = HeaderBytes + Encoding.UTF8.GetMaxByteCount(text.Length);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
        try
        {
            int written = Encoding.UTF8.GetBytes(text, buffer.AsSpan(HeaderBytes));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, sizeof(int)), rawKind);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(sizeof(int), sizeof(int)), written);
            digest.AppendData(buffer.AsSpan(0, HeaderBytes + written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void AppendBannerVerdict(IncrementalHash digest, SyntaxTree tree)
    {
        Span<byte> verdict = stackalloc byte[1];
        verdict[0] = GeneratedSourceSignals.HasGeneratedBanner(tree) ? (byte)1 : (byte)0;
        digest.AppendData(verdict);
    }
}
