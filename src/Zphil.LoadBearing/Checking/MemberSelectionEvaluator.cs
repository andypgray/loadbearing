using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     Resolves a <see cref="MemberSelection" /> to the ordered list of <see cref="MemberNode" />s it
///     names (GRAMMAR §4.6): the declared members of its underlying type selection, narrowed by the
///     projection's kind filter and then by each member adjective in authoring order. Nouns/adjectives on
///     the <em>type</em> side are resolved by <see cref="SelectionEvaluator" /> (subject universe:
///     solution-declared types, whose <see cref="TypeNode.Members" /> are populated; externals carry
///     none). Name adjectives match ordinally (globs via <see cref="TypeNamePattern" />); <c>Returning</c>
///     compares a method's <see cref="IMemberInfo.ReturnTypeFullName" /> against the anchors' definition
///     FQNs (<see cref="SelectionEvaluator.DefinitionFullName" /> — a closed-generic anchor throws the
///     check-time backstop, GRAMMAR §4.6); the member <c>Where</c> runs through the guarded predicate
///     invoke. The result is ordered by <c>(DeclaringType.FullName, SymbolId)</c> so violations are
///     deterministic.
/// </summary>
internal sealed class MemberSelectionEvaluator
{
    private readonly SelectionEvaluator _selections;

    internal MemberSelectionEvaluator(SelectionEvaluator selections)
    {
        _selections = selections;
    }

    /// <summary>
    ///     The declared members the selection ranges over, deterministically ordered. Throws
    ///     <see cref="RuleEvaluationException" /> when a <c>Returning</c> anchor is a closed generic
    ///     construction (the check-time backstop for GRAMMAR §8 item 14) — the checker surfaces it as a
    ///     <see cref="ViolationKind.RuleError" /> rather than crashing the run.
    /// </summary>
    internal IReadOnlyList<MemberNode> Resolve(MemberSelection selection)
    {
        var sourceTypes = _selections.Evaluate(selection.Source, SelectionPosition.Subject);

        var members = sourceTypes.SelectMany(type => type.Members).Where(KindFilter(selection.Kind));
        foreach (MemberAdjective adjective in selection.Adjectives) members = ApplyAdjective(members, adjective);

        return members
            .OrderBy(DeclaringFullName, StringComparer.Ordinal)
            .ThenBy(member => member.SymbolId, StringComparer.Ordinal)
            .ToList();
    }

    private static Func<MemberNode, bool> KindFilter(MemberKindFilter kind)
    {
        return kind switch
        {
            MemberKindFilter.Method => member => member.Kind == MemberKind.Method,
            MemberKindFilter.Property => member => member.Kind == MemberKind.Property,
            MemberKindFilter.Field => member => member.Kind == MemberKind.Field,
            MemberKindFilter.Event => member => member.Kind == MemberKind.Event,
            _ => _ => true // MemberKindFilter.Any — the unrestricted .Members projection
        };
    }

    private static IEnumerable<MemberNode> ApplyAdjective(IEnumerable<MemberNode> current, MemberAdjective adjective)
    {
        switch (adjective)
        {
            case MemberWithSuffixAdjective suffix:
                return current.Where(member => member.Name.EndsWith(suffix.Suffix, StringComparison.Ordinal));
            case MemberWithPrefixAdjective prefix:
                return current.Where(member => member.Name.StartsWith(prefix.Prefix, StringComparison.Ordinal));
            case MemberWithNameMatchingAdjective matching:
                var pattern = new TypeNamePattern(matching.Glob);
                return current.Where(member => pattern.Matches(member.Name));
            case ReturningAdjective returning:
                // Anchor keys resolve eagerly (before the lazy Where), so a closed-generic anchor throws the
                // backstop here — during resolution — exactly like a closed-generic type noun (§4.1).
                var anchors = ReturningAnchors(returning.Types);
                return current.Where(member => member.ReturnTypeFullName is { } returnType && anchors.Contains(returnType));
            case MemberWhereAdjective where:
                return current.Where(member => SelectionEvaluator.InvokePredicate(where.Predicate, member, "Where"));
            default:
                // Fail closed (M4): an unknown member adjective would silently widen the member set. The kind
                // filter's Any arm (KindFilter) is a legitimate total match; a missing adjective arm is a bug —
                // throw (ArchChecker contains it per-rule) rather than pass the un-narrowed set through.
                throw new InvalidOperationException($"Unhandled member adjective '{adjective.GetType().Name}'.");
        }
    }

    // The definition-level FQNs a .Returning anchor set matches against, byte-identical to the extraction's
    // ReturnTypeFullName form (GRAMMAR §4.6): a non-generic anchor is exact, an open-generic anchor matches
    // any construction (its declared-type-parameter definition name). DefinitionFullName refuses a closed
    // generic here — the check-time backstop for the spec-build refusal (GRAMMAR §8 item 14).
    private static HashSet<string> ReturningAnchors(IReadOnlyList<Type> types)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        foreach (Type type in types)
            anchors.Add(SelectionEvaluator.DefinitionFullName(
                type, "member return-type matching is definition-level. Anchor on the open definition instead."));

        return anchors;
    }

    private static string DeclaringFullName(MemberNode member)
    {
        // A member's DeclaringType is always the TypeNode that owns it (reference equality, MemberNode remarks).
        return ((TypeNode)member.DeclaringType).FullName;
    }
}