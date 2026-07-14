using System.Text.RegularExpressions;

namespace Zphil.LoadBearing.Validation;

/// <summary>
///     The rule-ID grammar (GRAMMAR §8 item 7) and the extends-scope predicate (§7). IDs follow
///     the <c>area/rule-name</c> convention: lowercase alphanumerics and hyphens, slash-separated.
/// </summary>
internal static class RuleIdSyntax
{
    /// <summary>The ID pattern: <c>^[a-z0-9-]+(/[a-z0-9-]+)*$</c>.</summary>
    internal const string Pattern = "^[a-z0-9-]+(/[a-z0-9-]+)*$";

    private static readonly Regex Matcher = new(Pattern, RegexOptions.CultureInvariant);

    /// <summary>Whether an ID is well-formed.</summary>
    internal static bool IsValid(string? id)
    {
        return id != null && Matcher.IsMatch(id);
    }

    /// <summary>
    ///     Whether a declared rule ID equals a scope ID or falls under its reserved
    ///     <c>{scopeId}/…</c> namespace (§7). The generated <c>containment</c>/<c>tripwire</c>
    ///     children live there, so a declared ID may not.
    /// </summary>
    internal static bool ExtendsScope(string ruleId, string scopeId)
    {
        return string.Equals(ruleId, scopeId, StringComparison.Ordinal)
               || ruleId.StartsWith(scopeId + "/", StringComparison.Ordinal);
    }
}