using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     Resolves the <c>--add</c> names against a rule's current violations (from the same
///     empty-baseline check the whole <c>baseline</c> command runs on). A name matches a type when it
///     ordinally equals the <see cref="TypeNode.FullName" /> or the <see cref="TypeNode.SymbolId" />;
///     an edge's source and target must match the <em>same</em> violation. Zero matches and ambiguous
///     matches (two distinct identities) are loud <see cref="UserErrorException" />s listing the
///     candidates — the baseline records observed reality, so the valve only admits what is actually
///     red. Pure over the in-memory results, so ambiguity is unit-testable with synthetic nodes.
/// </summary>
internal static class BaselineAddMatcher
{
    public static Violation ResolveEdge(string ruleId, IReadOnlyList<Violation> violations, string source, string target)
    {
        var echo = $"--source '{source}' --target '{target}'";
        var candidates = violations
            .Where(v => v.Kind == ViolationKind.Reference && Matches(v.Source!, source) && Matches(v.Target!, target))
            .ToList();

        return Resolve(ruleId, violations, candidates, echo);
    }

    public static Violation ResolveSubject(string ruleId, IReadOnlyList<Violation> violations, string subject)
    {
        var echo = $"--subject '{subject}'";
        var candidates = violations
            .Where(v => v.Kind == ViolationKind.Shape && Matches(v.Subject!, subject))
            .ToList();

        return Resolve(ruleId, violations, candidates, echo);
    }

    private static Violation Resolve(string ruleId, IReadOnlyList<Violation> violations, IReadOnlyList<Violation> candidates, string echo)
    {
        var identities = candidates.Select(c => c.BaselineIdentity()!).Distinct().ToList();
        if (identities.Count == 0) throw new UserErrorException(NoMatch(ruleId, violations, echo));
        if (identities.Count > 1)
            throw new UserErrorException(
                $"{echo} matches more than one current violation of '{ruleId}': {RenderSymbols(identities)}. Use the 'T:' symbol ID form.");

        return candidates[0];
    }

    private static string NoMatch(string ruleId, IReadOnlyList<Violation> violations, string echo)
    {
        var current = violations
            .Where(v => v.BaselineIdentity() is not null)
            .Select(FullNameForm)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var head = $"no current violation of '{ruleId}' matches {echo} — the baseline records observed reality";
        if (current.Count == 0) return $"{head}; the rule currently has no violations.";

        string list = string.Join("\n", current.Select(name => $"  {name}"));
        return $"{head}. Current violations:\n{list}";
    }

    private static string RenderSymbols(IEnumerable<BaselineEntry> identities)
    {
        return string.Join(", ", identities.Select(SymbolForm).OrderBy(symbol => symbol, StringComparer.Ordinal));
    }

    private static string FullNameForm(Violation violation)
    {
        return violation.Kind == ViolationKind.Shape
            ? violation.Subject!.FullName
            : $"{violation.Source!.FullName} -> {violation.Target!.FullName}";
    }

    private static string SymbolForm(BaselineEntry identity)
    {
        return identity.Subject is not null ? identity.Subject : $"{identity.Source} -> {identity.Target}";
    }

    private static bool Matches(TypeNode node, string name)
    {
        return string.Equals(node.FullName, name, StringComparison.Ordinal)
               || string.Equals(node.SymbolId, name, StringComparison.Ordinal);
    }
}