namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     One <c>file:line</c> reference or declaration position inside a fragment: an absolute OS-native
///     file path stored verbatim (no normalization) and a 1-based line. The pure-data counterpart of
///     <see cref="Zphil.LoadBearing.Codebase.SourceLocation" />.
/// </summary>
/// <remarks>
///     <see cref="CompareTo" /> is the pinned site ordering — ordinal by <see cref="File" />, then by
///     <see cref="Line" /> — so a <see cref="System.Collections.Generic.SortedSet{T}" /> of sites both
///     orders and de-duplicates identically on the per-input extraction side and the merge side. Note the
///     ordinal file compare means a <c>*.Validation.cs</c> part sorts before its <c>*.cs</c> sibling
///     (<c>'V' &lt; 'c'</c>).
/// </remarks>
internal sealed record FragmentSite(string File, int Line) : IComparable<FragmentSite>
{
    public int CompareTo(FragmentSite? other)
    {
        if (other is null) return 1;
        int byFile = string.CompareOrdinal(File, other.File);
        return byFile != 0 ? byFile : Line.CompareTo(other.Line);
    }
}