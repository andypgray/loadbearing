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
///     lists the distinct member IDs to retry with (GRAMMAR §4.5). A <c>--subject</c> likewise matches a
///     <see cref="ViolationKind.MemberShape" /> by the member's full name (no parens —
///     <c>MyApp.Web.HomeController.Save</c>) or its member symbol ID (<c>M:</c>/<c>P:</c>/<c>F:</c>/<c>E:</c>),
///     with the same overload-ambiguity behavior (GRAMMAR §4.6). Zero matches and ambiguous matches
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
            .Where(v => MatchesSubject(v, subject))
            .ToList();

        return Resolve(ruleId, violations, candidates, echo);
    }

    // A --subject matches a type-shape violation on its subject type (FullName or T: symbol ID), or a
    // member-shape violation on its subject member (member full name, no parens, or member symbol ID,
    // GRAMMAR §4.6) — one full name covering every overload, exactly as a member --target does (§4.5).
    private static bool MatchesSubject(Violation violation, string subject)
    {
        return violation.Kind switch
        {
            ViolationKind.Shape => Matches(violation.Subject!, subject),
            ViolationKind.MemberShape => MatchesMemberSubject(violation.SubjectMember!, subject),
            _ => false
        };
    }

    private static bool MatchesMemberSubject(MemberNode member, string subject)
    {
        return string.Equals($"{((TypeNode)member.DeclaringType).FullName}.{member.Name}", subject, StringComparison.Ordinal)
               || string.Equals(member.SymbolId, subject, StringComparison.Ordinal);
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

    // A --source/--target pair matches a reference edge on both type endpoints, a construction edge on both
    // type endpoints likewise (the constructed type rides the Target slot, GRAMMAR §4.5), an injection edge on
    // both type endpoints likewise (the injected parameter type rides the Target slot, §4.7), a catch edge on
    // both type endpoints likewise (the caught type rides the Target slot, §4.8), a throw edge on both type
    // endpoints likewise (the thrown type rides the Target slot, §4.8), an exposure edge on both type endpoints
    // likewise (the exposed type rides the Target slot, §4.9), or a member-use edge on the source type
    // and the banned member (by full name or member symbol ID, §4.5). The construction/injection/catch/thrown/
    // exposed types need no dedicated FullNameForm arm — the default `Source -> Target` covers them (verified by test).
    private static bool MatchesEdge(Violation violation, string source, string target)
    {
        return violation.Kind switch
        {
            ViolationKind.Reference => Matches(violation.Source!, source) && Matches(violation.Target!, target),
            ViolationKind.Construction => Matches(violation.Source!, source) && Matches(violation.Target!, target),
            ViolationKind.Injection => Matches(violation.Source!, source) && Matches(violation.Target!, target),
            ViolationKind.Catch => Matches(violation.Source!, source) && Matches(violation.Target!, target),
            ViolationKind.Throw => Matches(violation.Source!, source) && Matches(violation.Target!, target),
            ViolationKind.Expose => Matches(violation.Source!, source) && Matches(violation.Target!, target),
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

    // A violation's full-name listing form: the subject for a type shape, the member subject for a member
    // shape, Source -> Target for a reference edge, Source -> member display for a member use. Shared with
    // BaselineRunner's added-entry echo so the success message and the no-match candidate list render one
    // way for every kind --add resolves.
    internal static string FullNameForm(Violation violation)
    {
        return violation.Kind switch
        {
            ViolationKind.Shape => violation.Subject!.FullName,
            ViolationKind.MemberShape => MemberSubjectDisplay(violation.SubjectMember!),
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

    // The offending member subject, same declaring-type-dot-member, () iff a method convention — so a
    // member-shape candidate list echoes 'Save()' exactly as 'loadbearing check' renders it (GRAMMAR §4.6).
    private static string MemberSubjectDisplay(MemberNode member)
    {
        string suffix = member.Kind == MemberKind.Method ? "()" : string.Empty;
        return $"{((TypeNode)member.DeclaringType).FullName}.{member.Name}{suffix}";
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