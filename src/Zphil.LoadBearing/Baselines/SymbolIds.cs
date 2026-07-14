using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>Helpers for the Roslyn <c>DocumentationCommentId</c> strings baselines key on (DESIGN.md §8).</summary>
public static class SymbolIds
{
    /// <summary>
    ///     A human-facing rendering of a symbol ID: the leading <c>T:</c> stripped from a type ID
    ///     (leaving the full type name), everything else verbatim (an <c>unresolved:{FullName}</c>
    ///     fallback prints as-is). Used for <c>status</c> and other human output; the stored key is
    ///     always the raw ID.
    /// </summary>
    public static string Display(string symbolId)
    {
        Guard.NotNull(symbolId, nameof(symbolId));

        return symbolId.StartsWith("T:", StringComparison.Ordinal) ? symbolId.Substring(2) : symbolId;
    }
}