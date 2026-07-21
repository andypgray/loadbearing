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
    private readonly CodebaseModel _model;
    private readonly List<TypeNode> _solutionDeclared;

    internal SelectionEvaluator(CodebaseModel model)
    {
        _model = model;
        _solutionDeclared = model.Types.Where(t => !t.IsExternal).ToList();
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
            var members = new HashSet<TypeNode>();
            foreach (Selection member in union.Parts) members.UnionWith(Evaluate(member, position));
            return members;
        }

        IEnumerable<TypeNode> universe = position == SelectionPosition.Subject ? _solutionDeclared : _model.Types;
        var current = ByNoun(selection.Noun, universe);
        foreach (SelectionAdjective adjective in selection.Adjectives) current = ApplyAdjective(current, adjective);

        return new HashSet<TypeNode>(current);
    }

    private static IEnumerable<TypeNode> ByNoun(SelectionNoun noun, IEnumerable<TypeNode> universe)
    {
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
                return universe.Where(t => string.Equals(t.ProjectName, project.Name, StringComparison.Ordinal));
            case TypeNoun typeNoun:
                string fullName = TypeNounFullName(typeNoun.Type);
                return universe.Where(t => string.Equals(t.FullName, fullName, StringComparison.Ordinal));
            default:
                // Fail closed (M4): the closed noun hierarchy makes this arm unreachable for any v1 noun. An
                // unknown noun means a new noun without a switch arm; throw rather than select nothing (which
                // would vacuously pass every shape verb over an empty subject). ArchChecker contains it per-rule.
                throw new InvalidOperationException($"Unhandled selection noun '{noun.GetType().Name}'.");
        }
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
                var interfaceMatch = InterfaceMatcher(implementing.Type);
                return current.Where(interfaceMatch);
            case DerivedFromAdjective derivedFrom:
                var baseMatch = BaseTypeMatcher(derivedFrom.Type);
                return current.Where(baseMatch);
            case AttributedWithAdjective attributedWith:
                var attributeMatch = AttributeMatcher(attributedWith.Type);
                return current.Where(attributeMatch);
            case ExceptAdjective except:
                var excluded = Evaluate(except.Payload, SelectionPosition.Target);
                return current.Where(t => !excluded.Contains(t));
            case WhereAdjective where:
                return current.Where(t => InvokePredicate(where.Predicate, t, "Where"));
            default:
                // Fail closed (M4): an unknown adjective would silently widen the selection — and in a
                // MustOnly* target position a silently-widened allow-set is fail-open enforcement. A missing
                // arm is a bug; throw (ArchChecker contains it per-rule) rather than pass the widened set through.
                throw new InvalidOperationException($"Unhandled selection adjective '{adjective.GetType().Name}'.");
        }
    }

    // The three hierarchy matchers share one shape: an open generic definition matches on the
    // definition FullName ("any construction"); a closed or non-generic type matches on the
    // constructed FullName ("that construction exactly") — GRAMMAR §5.2. FullDisplay runs once,
    // eagerly, so an unrepresentable type throws before any node is tested. Shared with the
    // MustImplement/MustDeriveFrom/MustBeAttributedWith constraint verbs.
    internal static Func<TypeNode, bool> InterfaceMatcher(Type type)
    {
        string key = TypeName.FullDisplay(type);
        return type.IsGenericTypeDefinition
            ? t => t.AllInterfaces.Any(c => c.Definition.FullName == key)
            : t => t.AllInterfaces.Any(c => c.FullName == key);
    }

    internal static Func<TypeNode, bool> BaseTypeMatcher(Type type)
    {
        string key = TypeName.FullDisplay(type);
        return type.IsGenericTypeDefinition
            ? t => t.BaseTypeChain.Any(c => c.Definition.FullName == key)
            : t => t.BaseTypeChain.Any(c => c.FullName == key);
    }

    internal static Func<TypeNode, bool> AttributeMatcher(Type type)
    {
        string key = TypeName.FullDisplay(type);
        return type.IsGenericTypeDefinition
            ? t => t.AttributeConstructions.Any(c => c.Definition.FullName == key)
            : t => t.AttributeConstructions.Any(c => c.FullName == key);
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