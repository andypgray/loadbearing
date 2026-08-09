using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     Resolves a <see cref="Selection" /> to the set of <see cref="TypeNode" />s it names, given its
///     <see cref="SelectionPosition" /> (GRAMMAR §4.1, §5.1–§5.2). Nouns pick the candidate set;
///     adjectives narrow it; <c>Except</c> subtracts; the hierarchy adjectives read the
///     construction lists (open definition matches <see cref="TypeConstruction.Definition" />, a
///     closed construction matches <see cref="TypeConstruction.FullName" />). External nodes carry
///     empty construction lists, so hierarchy adjectives never match them (documented boundary).
/// </summary>
internal sealed class SelectionEvaluator
{
    private readonly Dictionary<string, TypeNode> _byFullName;
    private readonly ILookup<string, TypeNode> _byProjectName;
    private readonly CodebaseModel _model;
    private readonly List<TypeNode> _solutionDeclared;

    internal SelectionEvaluator(CodebaseModel model)
    {
        _model = model;
        _solutionDeclared = model.Types.Where(t => !t.IsExternal).ToList();

        // The two noun indexes, built once per evaluator and immutable afterwards: a typeof or project
        // operand is otherwise a full linear scan of the type universe — the largest list in the model —
        // per operand per rule. Both preserve Types order within a key, so the sets they feed are
        // populated in exactly the order the equivalent Where scan populated them.
        _byFullName = new Dictionary<string, TypeNode>(model.Types.Count, StringComparer.Ordinal);
        foreach (TypeNode type in model.Types) _byFullName[type.FullName] = type;
        _byProjectName = model.Types.ToLookup(t => t.ProjectName, StringComparer.Ordinal);
    }

    /// <summary>Whether the operand is a pattern/glob selection (anything but a bare <c>typeof</c>) — the inert-warning gate.</summary>
    internal static bool IsPatternSelection(Selection selection)
    {
        return selection is UnionSelection || selection.Adjectives.Count > 0 || selection.Noun is not TypeNoun;
    }

    internal HashSet<TypeNode> Evaluate(Selection selection, SelectionPosition position)
    {
        if (selection is UnionSelection union)
        {
            var parts = new List<HashSet<TypeNode>>(union.Parts.Count);
            foreach (Selection member in union.Parts) parts.Add(Evaluate(member, position));

            return Unite(union, parts);
        }

        var current = ByNoun(selection.Noun, position);
        foreach (SelectionAdjective adjective in selection.Adjectives) current = ApplyAdjective(current, adjective);

        return new HashSet<TypeNode>(current);
    }

    /// <summary>
    ///     The second half of the union arm: fold the already-evaluated operand sets together and apply
    ///     the union's own adjectives. Union adjectives apply to the unioned set, never through each
    ///     operand (GRAMMAR §5.1): <c>AnyOf(a, b).Except(c)</c> is (a ∪ b) − c. Exposed so a caller that
    ///     has already evaluated the operands in the same position — the per-operand emptiness gate in
    ///     <see cref="ConstraintEvaluator" /> (GRAMMAR §9) — can finish the union without evaluating them
    ///     a second time.
    /// </summary>
    internal HashSet<TypeNode> Unite(UnionSelection union, IReadOnlyList<HashSet<TypeNode>> parts)
    {
        var members = new HashSet<TypeNode>();
        foreach (var part in parts) members.UnionWith(part);

        if (union.Adjectives.Count == 0) return members;

        IEnumerable<TypeNode> unioned = members;
        foreach (SelectionAdjective adjective in union.Adjectives) unioned = ApplyAdjective(unioned, adjective);

        return new HashSet<TypeNode>(unioned);
    }

    // Instance (not static) because the RegisteredNoun arm reads model-side registration facts
    // (CodebaseModel.ServiceRegistrations) — membership is many-to-many and never denormalized onto a
    // TypeNode (GRAMMAR §4.7). The other nouns select purely off the position-correct universe. Never an
    // iterator: TypeNounFullName must refuse a closed-generic noun eagerly, before any node is tested.
    private IEnumerable<TypeNode> ByNoun(SelectionNoun noun, SelectionPosition position)
    {
        bool subject = position == SelectionPosition.Subject;
        IEnumerable<TypeNode> universe = subject ? _solutionDeclared : _model.Types;
        switch (noun)
        {
            case TypesNoun:
                return universe.Where(t => !t.IsExternal); // arch.Types is solution-declared by definition (§5.1)
            case LayerNoun layer:
                var globs = layer.Globs.Select(g => new NamespacePattern(g)).ToList();
                return universe.Where(t => globs.Any(p => p.Matches(t.Namespace)));
            case NamespaceNoun ns:
                var pattern = new NamespacePattern(ns.Glob);
                return universe.Where(t => pattern.Matches(t.Namespace));
            case ProjectNoun project:
                // The ordinal ProjectName index, then the position filter — the same nodes in the same
                // order the universe scan yielded, because a lookup grouping keeps Types order.
                var declaring = _byProjectName[project.Name];
                return subject ? declaring.Where(t => !t.IsExternal) : declaring;
            case TypeNoun typeNoun:
                // Types is unique by FullName (same-FQN declarers are conflated at merge), so the scan
                // was a dictionary lookup wearing a Where: at most one node, position-filtered.
                string fullName = TypeNounFullName(typeNoun.Type);
                return _byFullName.TryGetValue(fullName, out TypeNode? named) && !(subject && named.IsExternal)
                    ? new[] { named }
                    : Array.Empty<TypeNode>();
            case RegisteredNoun registered:
                // Membership = service ∪ implementation FQNs of the recognized registrations at this lifetime
                // (null = any lifetime, §4.7). Filtering the position-correct universe (subject = solution-
                // declared, target = all types incl. externals) means an external registered type matches in
                // target position but never enters a subject — exactly the §4.1 universe discipline.
                var registeredNames = RegisteredFullNames(registered.Lifetime);
                return universe.Where(t => registeredNames.Contains(t.FullName));
            default:
                // Fail closed: the closed noun hierarchy makes this arm unreachable for any v1 noun. An
                // unknown noun means a new noun without a switch arm; throw rather than select nothing (which
                // would vacuously pass every shape verb over an empty subject). ArchChecker contains it per-rule.
                throw new InvalidOperationException($"Unhandled selection noun '{noun.GetType().Name}'.");
        }
    }

    // The FQN membership set of arch.Registered(lifetime) (GRAMMAR §4.7): the union of the service and
    // implementation full names of every recognized registration at the requested lifetime (null = all
    // lifetimes), skipping the null implementation of a factory/instance registration. The FQNs are already in
    // TypeNode.FullName form (definition-level, declared type-parameter names), so a node compares directly.
    private HashSet<string> RegisteredFullNames(Lifetime? lifetime)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ServiceRegistration registration in _model.ServiceRegistrations)
        {
            if (lifetime is { } wanted && registration.Lifetime != wanted) continue;
            names.Add(registration.ServiceFullName);
            if (registration.ImplementationFullName is { } implementation) names.Add(implementation);
        }

        return names;
    }

    private static string TypeNounFullName(Type type)
    {
        return DefinitionFullName(
            type,
            "v1 reference edges are type-level. Ban the open definition and/or the argument type separately.");
    }

    /// <summary>
    ///     The extraction-format FQN of a definition-level anchor type, refusing a closed generic
    ///     construction (which has no definition-level node, GRAMMAR §4.1/§4.5) as a
    ///     <see cref="RuleEvaluationException" /> — a <see cref="ViolationKind.RuleError" /> the checker
    ///     surfaces rather than crashing. The <paramref name="closedGenericGuidance" /> tail is spliced
    ///     after "is a closed generic construction; " so each caller (type-noun refusal, member-anchor
    ///     refusal) states its own actionable next step; an open definition resolves normally.
    /// </summary>
    internal static string DefinitionFullName(Type type, string closedGenericGuidance)
    {
        if (Generics.IsConstructed(type))
            throw new RuleEvaluationException(
                $"`{TypeName.Simple(type)}` is a closed generic construction; {closedGenericGuidance}");

        return TypeName.FullDisplay(type);
    }

    private IEnumerable<TypeNode> ApplyAdjective(IEnumerable<TypeNode> current, SelectionAdjective adjective)
    {
        switch (adjective)
        {
            case InNamespaceAdjective inNamespace:
                var pattern = new NamespacePattern(inNamespace.Glob);
                return current.Where(t => pattern.Matches(t.Namespace));
            case OfKindAdjective ofKind:
                return current.Where(t => t.Kind == ofKind.Kind);
            case WithSuffixAdjective suffix:
                return current.Where(t => t.Name.EndsWith(suffix.Suffix, StringComparison.Ordinal));
            case WithPrefixAdjective prefix:
                return current.Where(t => t.Name.StartsWith(prefix.Prefix, StringComparison.Ordinal));
            case WithNameMatchingAdjective matching:
                var namePattern = new TypeNamePattern(matching.Glob);
                return current.Where(t => namePattern.Matches(t.Name));
            case ImplementingAdjective implementing:
                var interfaceMatch = InterfaceMatcher(implementing.Anchor);
                return current.Where(interfaceMatch);
            case DerivedFromAdjective derivedFrom:
                var baseMatch = BaseTypeMatcher(derivedFrom.Anchor);
                return current.Where(baseMatch);
            case AttributedWithAdjective attributedWith:
                var attributeMatch = AttributeMatcher(attributedWith.Anchor);
                return current.Where(attributeMatch);
            case ExceptAdjective except:
                var excluded = Evaluate(except.Payload, SelectionPosition.Target);
                return current.Where(t => !excluded.Contains(t));
            case WhereAdjective where:
                return current.Where(t => InvokePredicate(where.Predicate, t, "Where"));
            case AuthoredAdjective:
                return current.Where(t => !t.IsGenerated);
            default:
                // Fail closed: an unknown adjective would silently widen the selection — and in a
                // MustOnly* target position a silently-widened allow-set is fail-open enforcement. A missing
                // arm is a bug; throw (ArchChecker contains it per-rule) rather than pass the widened set through.
                throw new InvalidOperationException($"Unhandled selection adjective '{adjective.GetType().Name}'.");
        }
    }

    // The three hierarchy matchers share one shape over one construction list each — interfaces, bases,
    // attributes. Every anchor form reduces to one of two comparisons (GRAMMAR §5.2):
    //
    //   * a STRING anchor names a definition, so it matches every construction of that definition and a
    //     constructed spelling matches nothing;
    //   * an open-generic typeof matches on the definition FullName ("any construction") and a closed or
    //     non-generic typeof on the constructed FullName ("that construction exactly").
    //
    // FullDisplay runs once, eagerly, so an unrepresentable typeof throws before any node is tested; a
    // string anchor needs no reflection at all, which is the whole point of the hatch. Shared with the
    // MustImplement/MustDeriveFrom/MustBeAttributedWith constraint verbs and their MustNot* twins.
    internal static Func<TypeNode, bool> InterfaceMatcher(TypeAnchor anchor)
    {
        return ConstructionMatcher(anchor, t => t.AllInterfaces);
    }

    internal static Func<TypeNode, bool> BaseTypeMatcher(TypeAnchor anchor)
    {
        return ConstructionMatcher(anchor, t => t.BaseTypeChain);
    }

    internal static Func<TypeNode, bool> AttributeMatcher(TypeAnchor anchor)
    {
        return ConstructionMatcher(anchor, t => t.AttributeConstructions);
    }

    /// <summary>
    ///     The name an anchor compares on, and whether it compares on the <em>definition</em> name — the
    ///     three-arm GRAMMAR §5.2 decision, stated once for every matcher that anchors on a
    ///     <see cref="TypeAnchor" /> (the type-side construction matchers and the member-side attribute
    ///     matcher). A string anchor names a definition; an open-generic <c>typeof</c> matches on the
    ///     definition name ("any construction"); anything else on the constructed name ("that construction
    ///     exactly"). <see cref="TypeName.FullDisplay" /> runs here, so an unrepresentable <c>typeof</c>
    ///     throws while the matcher is being built — before any subject is tested — and a string anchor
    ///     needs no reflection at all, which is the whole point of the hatch.
    /// </summary>
    internal static (string Key, bool OnDefinition) AnchorKey(TypeAnchor anchor)
    {
        if (anchor.DefinitionFullName is { } name) return (name, true);

        Type type = anchor.Type!;
        return (TypeName.FullDisplay(type), type.IsGenericTypeDefinition);
    }

    // The one comparison the three matchers share, parameterized by which construction list to read.
    private static Func<TypeNode, bool> ConstructionMatcher(
        TypeAnchor anchor, Func<TypeNode, IReadOnlyList<TypeConstruction>> constructions)
    {
        (string key, bool onDefinition) = AnchorKey(anchor);
        return onDefinition
            ? t => constructions(t).Any(c => c.Definition.FullName == key)
            : t => constructions(t).Any(c => c.FullName == key);
    }

    internal static bool InvokePredicate(Func<ITypeInfo, bool> predicate, TypeNode type, string hatch)
    {
        try
        {
            return predicate(type);
        }
        catch (Exception ex)
        {
            throw new RuleEvaluationException(
                $"the `{hatch}` predicate threw {ex.GetType().Name} on `{type.FullName}`: {ex.Message}");
        }
    }

    // The member-flavored guarded invoke (GRAMMAR §5.6): the member `.Where`/`.Must` escape hatches run
    // here so a throwing predicate becomes a RuleError naming the member, not an aborted run — the exact
    // shape of the type-side invoke above.
    internal static bool InvokePredicate(Func<IMemberInfo, bool> predicate, IMemberInfo member, string hatch)
    {
        try
        {
            return predicate(member);
        }
        catch (Exception ex)
        {
            throw new RuleEvaluationException(
                $"the `{hatch}` predicate threw {ex.GetType().Name} on `{MemberIdentity(member)}`: {ex.Message}");
        }
    }

    // The declaring-type-dot-member identity for a member escape-hatch error; the declaring type is always
    // a TypeNode (IMemberInfo.DeclaringType), so its FullName is available for a fully-qualified name.
    private static string MemberIdentity(IMemberInfo member)
    {
        return member.DeclaringType is TypeNode type ? $"{type.FullName}.{member.Name}" : member.Name;
    }
}
