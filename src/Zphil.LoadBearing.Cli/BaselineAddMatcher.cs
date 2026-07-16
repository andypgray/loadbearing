using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     Resolves the <c>--add</c> names against a rule's current violations (from the same
///     empty-baseline check the whole <c>baseline</c> command runs on). A name matches a type when it
///     ordinally equals the <see cref="TypeNode.FullName" /> or the <see cref="TypeNode.SymbolId" />;
///     an edge's source and target must match the <em>same</em> violation. A member <c>--target</c>
///     matches a <see cref="ViolationKind.MemberUse" /> when it equals the member's full name
///     (no parens — <c>System.DateTime.Now</c>) or its member symbol ID (<c>P:System.DateTime.Now</c>);
///     a full-name target naming an overloaded method matches every overload, so the ambiguity error
///     lists the distinct member IDs to retry with (GRAMMAR §4.5). Zero matches and ambiguous matches
///     (two distinct identities) are loud <see cref="UserErrorException" />s listing the candidates —
///     the baseline records observed reality, so the valve only admits what is actually red. Pure over
///     the in-memory results, so ambiguity is unit-testable with synthetic nodes.
/// </summary>
internal static class BaselineAddMatcher
{
    public static Violation ResolveEdge(string ruleId, IReadOnlyList<Violation> violations, string source, string target)
    {
        var echo = $"--source '{source}' --target '{target}'";
        var candidates = violations
            .Where(v => MatchesEdge(v, source, target))
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
                $"{echo} matches more than one current violation of '{ruleId}': {RenderSymbols(identities)}. Use the symbol ID form.");

        return candidates[0];
    }

    // A --source/--target pair matches a reference edge on both type endpoints, or a member-use edge on
    // the source type and the banned member (by full name or member symbol ID, GRAMMAR §4.5).
    private static bool MatchesEdge(Violation violation, string source, string target)
    {
        return violation.Kind switch
        {
            ViolationKind.Reference => Matches(violation.Source!, source) && Matches(violation.Target!, target),
            ViolationKind.MemberUse => Matches(violation.Source!, source) && MatchesMember(violation.Member!, target),
            _ => false
        };
    }

    private static bool MatchesMember(MemberReference member, string target)
    {
        return string.Equals($"{member.ContainingType.FullName}.{member.Name}", target, StringComparison.Ordinal)
               || string.Equals(member.SymbolId, target, StringComparison.Ordinal);
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

    // A violation's full-name listing form: the subject for a shape, Source -> Target for a reference
    // edge, Source -> member display for a member use. Shared with BaselineRunner's added-entry echo so
    // the success message and the no-match candidate list render one way for every kind --add resolves.
    internal static string FullNameForm(Violation violation)
    {
        return violation.Kind switch
        {
            ViolationKind.Shape => violation.Subject!.FullName,
            ViolationKind.MemberUse => $"{violation.Source!.FullName} -> {MemberDisplay(violation.Member!)}",
            _ => $"{violation.Source!.FullName} -> {violation.Target!.FullName}"
        };
    }

    // The banned member as declaring-type-dot-member, () iff a method — the parens-iff-method display of
    // the human report line, so the no-match candidate list reads exactly as 'loadbearing check' does.
    private static string MemberDisplay(MemberReference member)
    {
        string suffix = member.Kind == MemberKind.Method ? "()" : string.Empty;
        return $"{member.ContainingType.FullName}.{member.Name}{suffix}";
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