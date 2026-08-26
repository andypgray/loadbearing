using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli.Verbs;

/// <summary>
///     Resolves the <c>--add</c> names against a rule's current violations, from the same
///     empty-baseline check the whole <c>baseline</c> command runs on.
/// </summary>
/// <remarks>
///     <para>
///         <b>What matches what.</b> A name matches a type when it ordinally equals the
///         <see cref="TypeNode.FullName" /> or the <see cref="TypeNode.SymbolId" />; an edge's source and
///         target must match the <em>same</em> violation. A member <c>--target</c> matches a
///         <see cref="ViolationKind.MemberUse" /> when it equals the member's full name
///         (no parens — <c>System.DateTime.Now</c>) or its member symbol ID (<c>P:System.DateTime.Now</c>);
///         a full-name target naming an overloaded method matches every overload, so the ambiguity error
///         lists the distinct member IDs to retry with (GRAMMAR §4.5). A <c>--subject</c> likewise matches a
///         <see cref="ViolationKind.MemberShape" /> by the member's full name (no parens —
///         <c>MyApp.Web.HomeController.Save</c>) or its member symbol ID
///         (<c>M:</c>/<c>P:</c>/<c>F:</c>/<c>E:</c>), with the same overload-ambiguity behavior
///         (GRAMMAR §4.6).
///     </para>
///     <para>
///         Zero matches and ambiguous matches (two distinct identities) are loud
///         <see cref="UserErrorException" />s listing the candidates — the baseline records observed
///         reality, so the valve only admits what is actually red. Pure over the in-memory results, so
///         ambiguity is unit-testable with synthetic nodes.
///     </para>
/// </remarks>
internal static class BaselineAddMatcher
{
    public static Violation ResolveEdge(string ruleId, IReadOnlyList<Violation> violations, string source, string target)
    {
        var echo = $"--source '{source}' --target '{target}'";
        List<Violation> candidates = violations
            .Where(v => MatchesEdge(v, source, target))
            .ToList();

        return Resolve(ruleId, violations, candidates, echo);
    }

    public static Violation ResolveSubject(string ruleId, IReadOnlyList<Violation> violations, string subject)
    {
        var echo = $"--subject '{subject}'";
        List<Violation> candidates = violations
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
            ViolationKind.ProjectShape => MatchesProjectSubject(violation.SubjectProject!, subject),
            _ => false
        };
    }

    private static bool MatchesMemberSubject(MemberNode member, string subject)
    {
        return string.Equals($"{member.DeclaringTypeFullName}.{member.Name}", subject, StringComparison.Ordinal)
               || string.Equals(member.SymbolId, subject, StringComparison.Ordinal);
    }

    // A project's bare name or its identity form (GRAMMAR §4.10) — the same pair of spellings a type
    // subject takes, a readable name and the stored key. The key comes off the node, so this end and the
    // mint that writes the baseline entry cannot spell it apart.
    private static bool MatchesProjectSubject(ProjectNode project, string subject)
    {
        return string.Equals(project.Name, subject, StringComparison.Ordinal)
               || string.Equals(project.SymbolId, subject, StringComparison.Ordinal);
    }

    private static Violation Resolve(string ruleId, IReadOnlyList<Violation> violations, IReadOnlyList<Violation> candidates, string echo)
    {
        List<BaselineEntry> identities = candidates.Select(c => c.BaselineIdentity()!).Distinct().ToList();
        if (identities.Count == 0) throw new UserErrorException(NoMatch(ruleId, violations, echo));
        if (identities.Count > 1)
            throw new UserErrorException(
                $"{echo} matches more than one current violation of '{ruleId}': {RenderSymbols(identities)}. Use the symbol ID form.");

        return candidates[0];
    }

    // Every edge kind but MemberUse matches on both type endpoints, because the second type — constructed
    // (GRAMMAR §4.5), injected (§4.7), caught or thrown (§4.8), exposed (§4.9) — rides the Target slot;
    // MemberUse matches the source type and the banned member (by full name or member symbol ID, §4.5).
    // Those Target-slot kinds need no dedicated FullNameForm arm either: the default `Source -> Target`
    // covers them (verified by test).
    private static bool MatchesEdge(Violation violation, string source, string target)
    {
        return violation.Kind switch
        {
            ViolationKind.Reference
                or ViolationKind.Construction
                or ViolationKind.Injection
                or ViolationKind.Catch
                or ViolationKind.Throw
                or ViolationKind.Expose => Matches(violation.Source!, source) && Matches(violation.Target!, target),
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
        List<string> current = violations
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
    // way for every kind --add resolves. Members take the shared display, so a candidate list echoes
    // 'Save()' exactly as 'loadbearing check' renders it (GRAMMAR §4.5, §4.6).
    internal static string FullNameForm(Violation violation)
    {
        return violation.Kind switch
        {
            ViolationKind.Shape => violation.Subject!.FullName,
            ViolationKind.MemberShape => MemberDisplay.Of(violation.SubjectMember!),
            ViolationKind.MemberUse => $"{violation.Source!.FullName} -> {MemberDisplay.Of(violation.Member!)}",
            ViolationKind.ProjectShape => violation.Package is { } package
                ? $"{violation.SubjectProject!.Name} -> {package.Name}"
                : violation.SubjectProject!.Name,
            _ => $"{violation.Source!.FullName} -> {violation.Target!.FullName}"
        };
    }

    private static string SymbolForm(BaselineEntry identity)
    {
        return identity.Subject ?? $"{identity.Source} -> {identity.Target}";
    }

    private static bool Matches(TypeNode node, string name)
    {
        return string.Equals(node.FullName, name, StringComparison.Ordinal)
               || string.Equals(node.SymbolId, name, StringComparison.Ordinal);
    }
}
