using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     One <c>file:line</c> reference or declaration position inside a fragment: an absolute OS-native
///     file path stored verbatim (no normalization) and a 1-based line. The pure-data counterpart of
///     <see cref="Zphil.LoadBearing.Codebase.SourceLocation" />.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="CompareTo" /> is the pinned site ordering — ordinal by <see cref="File" />, then by
///         <see cref="Line" /> — so a <see cref="System.Collections.Generic.SortedSet{T}" /> of sites both
///         orders and de-duplicates identically on the per-input extraction side and the merge side. Note the
///         ordinal file compare means a <c>*.Validation.cs</c> part sorts before its <c>*.cs</c> sibling
///         (<c>'V' &lt; 'c'</c>).
///     </para>
///     <para>
///         A value type, because extraction mints one site per <em>mention</em> and the overwhelming majority
///         are immediately discarded as duplicates by the SortedSet they are offered to. Implementing
///         <see cref="IComparable{T}" /> is what keeps that set's <c>Comparer&lt;T&gt;.Default</c>
///         boxing-free.
///     </para>
/// </remarks>
internal readonly record struct FragmentSite(string File, int Line) : IComparable<FragmentSite>
{
    public int CompareTo(FragmentSite other)
    {
        int byFile = string.CompareOrdinal(File, other.File);
        return byFile != 0 ? byFile : Line.CompareTo(other.Line);
    }

    /// <summary>The site of a syntax node: its tree's path and the 1-based line its span starts on.</summary>
    internal static FragmentSite Of(SyntaxNode node)
    {
        return Of(node.SyntaxTree, node.Span);
    }

    /// <summary>
    ///     The site of <paramref name="span" /> inside <paramref name="tree" /> — for the callers holding a
    ///     token or a <see cref="Location" /> rather than a node, which pass its span.
    /// </summary>
    /// <remarks>
    ///     The <c>+ 1</c> is the whole reason both overloads exist: Roslyn reports a 0-based line and the
    ///     <see cref="Line" /> this record documents is 1-based, so every site pass used to restate that
    ///     conversion and any one of them could have drifted from the contract. Reading the line span off the
    ///     tree also skips the intermediate <see cref="Location" /> those passes allocated per mention.
    /// </remarks>
    internal static FragmentSite Of(SyntaxTree tree, TextSpan span)
    {
        int line = tree.GetLineSpan(span).StartLinePosition.Line + 1;
        return new FragmentSite(tree.FilePath, line);
    }
}
