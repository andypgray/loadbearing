using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>Helpers for the Roslyn <c>DocumentationCommentId</c> strings baselines key on.</summary>
public static class SymbolIds
{
    /// <summary>
    ///     A human-facing rendering of a symbol ID: the leading <c>DocumentationCommentId</c> prefix —
    ///     <c>T:</c> for a type, <c>M:</c>/<c>P:</c>/<c>F:</c>/<c>E:</c> for a member (GRAMMAR §4.5) —
    ///     stripped, leaving the full name; everything else verbatim.
    /// </summary>
    /// <remarks>
    ///     An <c>unresolved:{FullName}</c> fallback carries no such prefix and prints as-is. The
    ///     rendering is display-only: the stored key is always the raw ID.
    /// </remarks>
    public static string Display(string symbolId)
    {
        Guard.NotNull(symbolId, nameof(symbolId));

        // A DocId prefix is a single tag letter followed by ':'. The unresolved: fallback fails the
        // length-2 tag test (its second char is 'n', not ':'), so it prints unchanged.
        return symbolId.Length >= 2 && symbolId[1] == ':' && symbolId[0] is 'T' or 'M' or 'P' or 'F' or 'E'
            ? symbolId.Substring(2)
            : symbolId;
    }
}
