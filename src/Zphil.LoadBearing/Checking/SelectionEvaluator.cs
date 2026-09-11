using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     Resolves a <see cref="Selection" /> to the set of <see cref="TypeNode" />s it names, given its
///     <see cref="SelectionPosition" /> (GRAMMAR §4.1, §5.1–§5.2): nouns pick the candidate set,
///     adjectives narrow it, <c>Except</c> subtracts.
/// </summary>
/// <remarks>
///     The hierarchy adjectives read the construction lists (open definition matches
///     <see cref="TypeConstruction.Definition" />, a closed construction matches
///     <see cref="TypeConstruction.FullName" />). External nodes carry empty construction lists, so
///     hierarchy adjectives never match them (documented boundary).
/// </remarks>
internal sealed class SelectionEvaluator
{
    // The one noun MustBeRegistered resolves through, shared by every rule that uses the verb: the Scanned
    // memo is keyed on the noun by reference, so a per-call instance would rescan the universe per rule.
    private static readonly RegisteredNoun AnyLifetimeRegistered = new(null);

    private readonly ILookup<string, TypeNode> _byFullName;
    private readonly Dictionary<(SelectionNoun, SelectionPosition), IReadOnlyList<TypeNode>> _byNoun = new();
    private readonly CodebaseModel _model;
    private readonly List<TypeNode> _solutionDeclared;
    private ILookup<string, TypeNode>? _byProjectName;

    internal SelectionEvaluator(CodebaseModel model)
    {
        _model = model;

        // One pass over the type universe for two answers: the subject universe, and whether any type in
        // the model is declared by more than one project. The second is what lets SelectionAdmission skip
        // per-node attribution outright on a codebase where no source file compiles into two projects.
        var solutionDeclared = new List<TypeNode>();
        var multiplyDeclared = false;
        foreach (TypeNode type in model.Types)
        {
            if (!type.IsExternal) solutionDeclared.Add(type);
            if (type.AlsoDeclaredBy.Count > 0) multiplyDeclared = true;
        }

        _solutionDeclared = solutionDeclared;
        AnyMultiplyDeclared = multiplyDeclared;

        // The FQN noun index, built once per evaluator and immutable afterwards: a typeof operand is
        // otherwise a full linear scan of the type universe — the largest list in the model — per operand
        // per rule. It preserves Types order within a key, so the sets it feeds are populated in exactly
        // the order the equivalent Where scan populated them.
        //
        // A lookup rather than a dictionary because a full name is not unique: CodebaseModel.ShadowedNames
        // is the model's own roster of the names that are not (GRAMMAR §4.1). An indexer here would silently
        // keep whichever came last in Types order and make the other invisible to every typeof operand — a
        // ban that reaches only one of two types it names is worse than a slow scan. The lookup is built
        // unconditionally rather than gated on that roster: it is one O(n) pass either way, and a name maps
        // to its one node through the same call whether or not anything shadows it.
        _byFullName = model.Types.ToLookup(type => type.FullName, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Whether any type in the model is declared by more than one project (GRAMMAR §4.1) — one source
    ///     file compiled into several projects. False on an ordinary codebase, where it collapses every
    ///     attribution question a target-position test would otherwise have to ask.
    /// </summary>
    internal bool AnyMultiplyDeclared { get; }

    // The declarer index, built on first use in the same shape ConstraintEvaluator's edge indexes take, so
    // a spec with no project noun never pays the grouping pass. Keyed on EVERY project that declares a
    // type rather than only the one whose facts the node carries: one source file compiled into several
    // projects is one node (GRAMMAR §4.1), and a project selection over any of those projects names it. A
    // type is listed once per declarer and the declarers are distinct, so the lookup still preserves Types
    // order within a key — a grouping keeps first-seen order, and the outer sequence is still Types.
    private ILookup<string, TypeNode> ByProjectName =>
        _byProjectName ??= _model.Types
            .SelectMany(DeclaredBy, (type, declarer) => (Declarer: declarer, Type: type))
            .ToLookup(pair => pair.Declarer, pair => pair.Type, StringComparer.Ordinal);

    // Every project declaring one type, the winner first: the roster an arch.Project selection tests
    // against, and the same set TypeNode.IsDeclaredBy walks.
    private static IReadOnlyList<string> DeclaredBy(TypeNode type)
    {
        return [type.ProjectName, .. type.AlsoDeclaredBy];
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

        return Narrow(selection, ByNoun(selection.Noun, position));
    }

    /// <summary>
    ///     The second half of the union arm: folds the already-evaluated operand sets together and
    ///     applies the union's own adjectives.
    /// </summary>
    /// <remarks>
    ///     Union adjectives apply to the unioned set, never through each operand (GRAMMAR §5.1):
    ///     <c>AnyOf(a, b).Except(c)</c> is (a ∪ b) − c. Exposed so a caller that has already evaluated
    ///     the operands in the same position can finish the union without evaluating them a second time.
    /// </remarks>
    internal HashSet<TypeNode> Unite(UnionSelection union, IReadOnlyList<HashSet<TypeNode>> parts)
    {
        var members = new HashSet<TypeNode>();
        foreach (HashSet<TypeNode>? part in parts) members.UnionWith(part);

        if (union.Adjectives.Count == 0) return members;

        IEnumerable<TypeNode> unioned = members;
        foreach (SelectionAdjective adjective in union.Adjectives) unioned = ApplyAdjective(unioned, adjective);

        // Same as Evaluate's tail: the constructor names the deduplication rather than implying it.
        // ReSharper disable once UseCollectionExpression
        return new HashSet<TypeNode>(unioned);
    }

    /// <summary>
    ///     A selection's own adjectives applied to a candidate set already resolved in the same position —
    ///     the tail of <see cref="Evaluate" /> without its noun resolution.
    /// </summary>
    /// <remarks>
    ///     Exposed for the same reason as <see cref="Unite" />: a caller that already holds what the
    ///     selection's noun resolves to can finish the selection without resolving it a second time.
    /// </remarks>
    internal HashSet<TypeNode> Narrow(Selection selection, IEnumerable<TypeNode> candidates)
    {
        IEnumerable<TypeNode> current = candidates;
        foreach (SelectionAdjective adjective in selection.Adjectives) current = ApplyAdjective(current, adjective);

        // The constructor is the point: adjectives can yield the same node twice, and naming HashSet here
        // says the deduplication is deliberate where a spread would leave it to the return type.
        // ReSharper disable once UseCollectionExpression
        return new HashSet<TypeNode>(current);
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
                // arch.Types is solution-declared by definition (§5.1), so in subject position the universe
                // already IS the answer; only the target universe (which holds externals) needs the filter.
                return subject ? _solutionDeclared : Scanned(noun, position, () => universe.Where(t => !t.IsExternal));
            case LayerNoun { Definition: { } definition }:
                // A layer is transparent to its definition: it names what the definition names, in the
                // caller's position, and the layer's own adjectives are applied by Evaluate afterwards.
                // Unmemoized on purpose — the definition's own noun scan is memoized one level down, and a
                // definition may carry a Where predicate, which every other adjective re-runs per call.
                return Evaluate(definition, position);
            case LayerNoun layer:
                return Scanned(noun, position, () =>
                {
                    List<NamespacePattern> globs = layer.Globs.Select(g => new NamespacePattern(g)).ToList();
                    return universe.Where(t => MatchesAnyGlob(globs, t.Namespace));
                });
            case NamespaceNoun ns:
                return Scanned(noun, position, () =>
                {
                    var pattern = new NamespacePattern(ns.Glob);
                    return universe.Where(t => pattern.Matches(t.Namespace));
                });
            case ProjectNoun project:
                return ProjectMembers(project.Name, subject);
            case EachNoun family:
                // A family is the union of its cells to every reader but the three that read the
                // partition (GRAMMAR §5.1). Unmemoized on purpose, for the reason the definition-layer arm
                // above is: a layer cell may carry a Where predicate through its definition, and a project
                // family's cells are decided by a project selection that may carry one directly.
                return FamilyMembers(family, subject, position);
            case TypeNoun typeNoun:
                // The scan is a lookup wearing a Where: every node carrying the name, position-filtered.
                // Usually that is one — one source file compiled into several projects is conflated at merge,
                // and a name nothing declares is a single external. It is two where a project declares a name
                // a referenced assembly also supplies (GRAMMAR §4.1), and naming BOTH is what the position
                // filter below then makes right: in subject position the source declaration alone survives,
                // and in target position a ban reaches either binding rather than whichever node sorted last.
                string fullName = TypeNounFullName(typeNoun.Type);
                return _byFullName[fullName].Where(named => !(subject && named.IsExternal));
            case RegisteredNoun registered:
                // Membership = service ∪ implementation FQNs of the recognized registrations at this lifetime
                // (null = any lifetime, §4.7). Filtering the position-correct universe (subject = solution-
                // declared, target = all types incl. externals) means an external registered type matches in
                // target position but never enters a subject — exactly the §4.1 universe discipline.
                return Scanned(noun, position, () =>
                {
                    HashSet<string> registeredNames = RegisteredFullNames(registered.Lifetime);
                    return universe.Where(t => registeredNames.Contains(t.FullName));
                });
            default:
                // Fail closed: the closed noun hierarchy makes this arm unreachable for any v1 noun. An
                // unknown noun means a new noun without a switch arm; throw rather than select nothing (which
                // would vacuously pass every shape verb over an empty subject). ArchChecker contains it per-rule.
                throw new InvalidOperationException($"Unhandled selection noun '{noun.GetType().Name}'.");
        }
    }

    /// <summary>
    ///     The cells of a family subject, as selections, in the order the family declares or resolves
    ///     them (GRAMMAR §5.1) — the layers themselves, or one bare project noun per project the family's
    ///     project selection names.
    /// </summary>
    /// <remarks>
    ///     The one place cells are minted, so the three readers of the partition — the self-allowance, the
    ///     leaf verbs and the emptiness gate — all range over the same list, resolved once per rule. A
    ///     project cell is minted as the selection <c>arch.Project(name)</c> would be, stamped with the
    ///     family's own owner, which is what makes <see cref="SelectionAdmission.Collect" /> stage its
    ///     project head and §4.1 attribution judge each instance at its declarer.
    /// </remarks>
    internal IReadOnlyList<Selection> Cells(Selection family)
    {
        var noun = (EachNoun)family.Noun;
        if (noun.Layers is { } layers) return layers;

        IReadOnlyList<ProjectNode> projects = ProjectSelectionEvaluator.Resolve(noun.Projects!, _model.Projects);
        return projects
            .Select(project => (Selection)new RefinedSelection(
                family.Owner, new ProjectNoun(project.Name), Array.Empty<SelectionAdjective>()))
            .ToList();
    }

    // A family's membership: the union of its cells, each resolved in the caller's position. The layer
    // form evaluates the cell selection (so a definition-defined cell stays transparent to its
    // definition); the project form takes the project noun's own path per named project, which is what
    // makes a family of projects name exactly what a union of project nouns would.
    private IEnumerable<TypeNode> FamilyMembers(EachNoun family, bool subject, SelectionPosition position)
    {
        var members = new HashSet<TypeNode>();
        if (family.Layers is { } layers)
        {
            foreach (Layer cell in layers) members.UnionWith(Evaluate(cell, position));

            return members;
        }

        foreach (ProjectNode project in ProjectSelectionEvaluator.Resolve(family.Projects!, _model.Projects))
            members.UnionWith(ProjectMembers(project.Name, subject));

        return members;
    }

    // The ordinal declarer index, then the position filter — the same nodes in the same order the
    // universe scan yielded, because a lookup grouping keeps Types order.
    private IEnumerable<TypeNode> ProjectMembers(string name, bool subject)
    {
        IEnumerable<TypeNode> declaring = ByProjectName[name];
        return subject ? declaring.Where(t => !t.IsExternal) : declaring;
    }

    // The scanning nouns' memo, keyed on (noun, position): every one of them walks the whole position-
    // correct universe, and a spec names the same noun in rule after rule. Caching at the NOUN level is
    // sound because a noun carries no user predicate — a Where or Must lambda arrives as an adjective, so
    // one noun in one position always scans to the same list. The materialized list is read-only to every
    // consumer (Evaluate copies it into a HashSet, ApplyAdjective wraps it in a Where), so sharing it is safe.
    private IReadOnlyList<TypeNode> Scanned(
        SelectionNoun noun, SelectionPosition position, Func<IEnumerable<TypeNode>> scan)
    {
        (SelectionNoun noun, SelectionPosition position) key = (noun, position);
        if (_byNoun.TryGetValue(key, out IReadOnlyList<TypeNode>? cached)) return cached;

        List<TypeNode> scanned = scan().ToList();
        _byNoun[key] = scanned;
        return scanned;
    }

    // A layer's glob scan: an index walk rather than a LINQ Any, because it runs once per type in the
    // universe over a list that is usually a single glob.
    private static bool MatchesAnyGlob(IReadOnlyList<NamespacePattern> globs, string ns)
    {
        for (var i = 0; i < globs.Count; i++)
            if (globs[i].Matches(ns))
                return true;

        return false;
    }

    /// <summary>
    ///     The nodes <c>arch.Registered()</c> names at any lifetime, in the given position (GRAMMAR §4.7) —
    ///     the set <c>MustBeRegistered</c> tests its subjects against.
    /// </summary>
    /// <remarks>
    ///     Resolved through the noun's own arm rather than off the registration facts directly, so the verb's
    ///     membership IS the noun's and the two cannot drift.
    /// </remarks>
    internal HashSet<TypeNode> RegisteredMembers(SelectionPosition position)
    {
        // A set rather than the memo's list because every subject is tested against it; the constructor
        // names that where a spread would leave it to the return type.
        // ReSharper disable once UseCollectionExpression
        return new HashSet<TypeNode>(ByNoun(AnyLifetimeRegistered, position));
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

    /// <summary>
    ///     The anchor face of <see cref="DefinitionFullName(Type,string)" /> — the one helper both member
    ///     verbs resolve an anchor through (GRAMMAR §5.2, §4.6). A string anchor already <em>is</em> the
    ///     definition FQN, so it passes through verbatim and needs no reflection at all, which is the
    ///     escape hatch's whole point; a <c>typeof</c> anchor resolves reflectively and keeps the
    ///     closed-generic refusal.
    /// </summary>
    internal static string DefinitionFullName(TypeAnchor anchor, string closedGenericGuidance)
    {
        return anchor.DefinitionFullName ?? DefinitionFullName(anchor.Type!, closedGenericGuidance);
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
            case NamedAdjective named:
                // The same simple-name universe the glob form matches: no generic arity backtick, and a
                // nested type by its leaf — so a name reaches every type carrying it, in every namespace.
                var names = new HashSet<string>(named.Names, StringComparer.Ordinal);
                return current.Where(t => names.Contains(t.Name));
            case ImplementingAdjective implementing:
                Func<TypeNode, bool> interfaceMatch = InterfaceMatcher(implementing.Anchor);
                return current.Where(interfaceMatch);
            case DerivedFromAdjective derivedFrom:
                Func<TypeNode, bool> baseMatch = BaseTypeMatcher(derivedFrom.Anchor);
                return current.Where(baseMatch);
            case AttributedWithAdjective attributedWith:
                Func<TypeNode, bool> attributeMatch = AttributeMatcher(attributedWith.Anchor);
                return current.Where(attributeMatch);
            case ExceptAdjective except:
                HashSet<TypeNode> excluded = Evaluate(except.Payload, SelectionPosition.Target);
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
    // attributes. Which name each anchor form compares on, and why FullDisplay runs eagerly, is stated
    // once on AnchorKey (GRAMMAR §5.2).
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
    ///     <see cref="TypeAnchor" />.
    /// </summary>
    /// <remarks>
    ///     A string anchor names a definition, so it matches every construction of that definition and a
    ///     constructed spelling matches nothing; an open-generic <c>typeof</c> matches on the definition
    ///     name ("any construction"); anything else on the constructed name ("that construction exactly").
    ///     <see cref="TypeName.FullDisplay" /> runs here, so an unrepresentable <c>typeof</c> throws while
    ///     the matcher is being built — before any subject is tested — and a string anchor needs no
    ///     reflection at all, which is the whole point of the hatch.
    /// </remarks>
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
        Func<TypeConstruction, string> nameOf = onDefinition ? c => c.Definition.FullName : c => c.FullName;
        return t =>
        {
            IReadOnlyList<TypeConstruction>? candidates = constructions(t);
            for (var i = 0; i < candidates.Count; i++)
                if (nameOf(candidates[i]) == key)
                    return true;

            return false;
        };
    }

    internal static bool InvokePredicate(Func<ITypeInfo, bool> predicate, TypeNode type, string hatch)
    {
        return Invoke<TypeNode>(predicate, type, hatch, subject => subject.FullName);
    }

    // The member-flavored guarded invoke (GRAMMAR §5.6): the member `.Where`/`.Must` escape hatches run
    // here so a throwing predicate becomes a RuleError naming the member, not an aborted run.
    internal static bool InvokePredicate(Func<IMemberInfo, bool> predicate, IMemberInfo member, string hatch)
    {
        return Invoke(predicate, member, hatch, MemberIdentity);
    }

    // The project-flavored guarded invoke (GRAMMAR §5.6, §4.10). A project's own name is its whole
    // identity, so there is no qualified form to compose.
    internal static bool InvokePredicate(Func<IProjectInfo, bool> predicate, IProjectInfo project, string hatch)
    {
        return Invoke(predicate, project, hatch, subject => subject.Name);
    }

    // The guard the three strata share (GRAMMAR §5.6): a predicate that throws becomes a RuleEvaluationException
    // the checker turns into a RuleError, so one bad lambda reds its own rule instead of aborting the run.
    // Only the identity in the message differs per stratum, which is what `identityOf` supplies — and it is
    // read in the catch, so a subject whose identity is expensive costs nothing on the passing path.
    private static bool Invoke<T>(Func<T, bool> predicate, T subject, string hatch, Func<T, string> identityOf)
    {
        try
        {
            return predicate(subject);
        }
        catch (Exception ex)
        {
            throw new RuleEvaluationException(
                $"the `{hatch}` predicate threw {ex.GetType().Name} on `{identityOf(subject)}`: {ex.Message}");
        }
    }

    // The declaring-type-dot-member identity for a member escape-hatch error; the declaring type is always
    // a TypeNode (IMemberInfo.DeclaringType), so its FullName is available for a fully-qualified name.
    private static string MemberIdentity(IMemberInfo member)
    {
        return member.DeclaringType is TypeNode type ? $"{type.FullName}.{member.Name}" : member.Name;
    }
}
