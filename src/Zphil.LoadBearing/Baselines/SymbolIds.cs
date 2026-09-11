using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     Helpers for the symbol IDs a baseline entry keys on: Roslyn <c>DocumentationCommentId</c>
///     strings such as <c>T:MyApp.Billing.Invoice</c> or
///     <c>M:MyApp.Billing.Invoice.Post(System.DateTime)</c>.
/// </summary>
public static class SymbolIds
{
    /// <summary>
    ///     Renders a symbol ID for a human to read: strips the leading tag — <c>T:</c> for a type, and
    ///     <c>M:</c>, <c>P:</c>, <c>F:</c> or <c>E:</c> for a method, property, field or event — and
    ///     leaves the qualified name. Anything carrying no such tag comes back unchanged, including the
    ///     <c>unresolved:{name}</c> form an ID takes when the compiler could not resolve the symbol.
    ///     Display only: the key stored in a baseline file is always the raw ID.
    /// </summary>
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
