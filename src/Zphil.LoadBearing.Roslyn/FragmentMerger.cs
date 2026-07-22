using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Merges a set of per-input <see cref="CodebaseFragment" />s into one <see cref="CodebaseModel" />,
///     unifying by fully-qualified name to reproduce the global cross-input semantics the single-pass
///     builder produced:
///     <list type="bullet">
///         <item>
///             <b>M1</b> — fragments in input (ordinal-project) order; the first declarer wins a node's
///             facts and <c>ProjectName</c>; declaration sites union across declarers (partials across
///             projects, or a project's several target frameworks). A later declarer under a
///             <em>different</em> project name is same-FQN cross-project conflation: the facts still follow
///             the first declarer, and an advisory <see cref="CodebaseModel.MergeNotes">merge note</see>
///             records it (deduped per (FQN, loser); a project's own several target frameworks share its
///             name and so stay silent).
///         </item>
///         <item>
///             <b>M2</b> — hierarchy comes from the winning fragment only; <c>ResolveNode(fqn)</c> is the
///             declared-anywhere node, else a shallow external minted from a facts table. Because M1 fully
///             precedes M2, a type any fragment declares beats every fragment's external view of it, and
///             each <see cref="TypeConstruction.Definition" /> is the merged node instance.
///         </item>
///         <item>
///             <b>M3</b> — edge site-sets union per <c>(source, target)</c>, self-edges dropped, endpoints
///             the same <see cref="TypeNode" /> instances held by <see cref="CodebaseModel.Types" />.
///         </item>
///         <item>
///             <b>M4</b> — member-use edges (GRAMMAR §4.5): site-sets union per
///             <c>
///                 (source, member
///                 DocumentationCommentId)
///             </c>
///             , same-type uses dropped, with one shared
///             <see cref="MemberReference" /> instance per distinct SymbolId and every endpoint a merged node.
///         </item>
///         <item>
///             <b>M5</b> — declared members (GRAMMAR §4.6): the winning fragment's member inventory becomes
///             the node's <see cref="TypeNode.Members" /> list (winner-only, like M2), each
///             <see cref="MemberNode.DeclaringType" /> the same merged node; externals keep an empty list.
///         </item>
///         <item>
///             <b>M6</b> — construction edges (GRAMMAR §4.5): site-sets union per
///             <c>(source, constructed)</c>, self-construction dropped, endpoints the same
///             <see cref="TypeNode" /> instances held by <see cref="CodebaseModel.Types" />.
///         </item>
///         <item>
///             <b>M7</b> — injection edges (GRAMMAR §4.7): site-sets union per <c>(source, injected)</c>,
///             self-injection dropped, endpoints the same <see cref="TypeNode" /> instances held by
///             <see cref="CodebaseModel.Types" /> (an external injected type resolves to a shared external node).
///         </item>
///         <item>
///             <b>M8</b> — registration facts (GRAMMAR §4.7): site-sets union per
///             <c>(lifetime, service FQN, implementation FQN)</c>, string-side (no node resolution), so a
///             registration reported by several fragments (or several sites) collapses to one fact.
///         </item>
///         <item>
///             <b>M9</b> — catch edges (GRAMMAR §4.8): site-sets union per <c>(source, caught)</c>,
///             self-catch dropped, endpoints the same <see cref="TypeNode" /> instances held by
///             <see cref="CodebaseModel.Types" /> (an external caught type resolves to a shared external node).
///         </item>
///         <item>
///             <b>M10</b> — throw edges (GRAMMAR §4.8): site-sets union per <c>(source, thrown)</c>,
///             self-throw dropped, endpoints the same <see cref="TypeNode" /> instances held by
///             <see cref="CodebaseModel.Types" /> (an external thrown type resolves to a shared external node).
///         </item>
///     </list>
///     One code path serves cold runs, the fast test path, and cache hits, so the cache
///     cannot change results by construction.
/// </summary>
/// <remarks>
///     A single documented, unobservable tie-break moves relative to the old single-pass builder: when the
///     same external FQN carries genuinely different facts across compilations (mixed assembly versions),
///     its facts are taken from the <em>first fragment in input order</em> that references it, rather than
///     from whichever compilation the builder's dictionary iteration happened to mint it from first. Both
///     are deterministic, and with a uniform reference closure (the norm) the facts are identical either
///     way, so no model observable — and no pinned test — can distinguish them.
/// </remarks>
internal static class FragmentMerger
{
    public static CodebaseModel Merge(IReadOnlyList<CodebaseFragment> fragments)
    {
        return new MergeState().Run(fragments);
    }

    private sealed class MergeState
    {
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeSites = new();
        private readonly Dictionary<(string Src, string Ctor), SortedSet<FragmentSite>> _constructorEdgeSites = new();
        private readonly Dictionary<string, SortedSet<FragmentSite>> _declarationSites = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Tgt), SortedSet<FragmentSite>> _edgeSites = new();
        private readonly Dictionary<string, FragmentExternal> _externalFacts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FragmentType> _hierarchy = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Injected), SortedSet<FragmentSite>> _injectionEdgeSites = new();
        private readonly Dictionary<(string Src, string MemberSymbolId), SortedSet<FragmentSite>> _memberEdgeSites = new();
        private readonly Dictionary<string, MemberEdgeFacts> _memberFacts = new(StringComparer.Ordinal);
        private readonly List<string> _mergeNotes = [];
        private readonly Dictionary<string, TypeNode> _nodes = new(StringComparer.Ordinal);
        private readonly HashSet<(string Fqn, string Loser)> _notedConflations = [];
        private readonly Dictionary<(Lifetime Lifetime, string Service, string? Impl), SortedSet<FragmentSite>> _registrationSites = new();
        private readonly Dictionary<(string Src, string Thrown), SortedSet<FragmentSite>> _throwEdgeSites = new();

        public CodebaseModel Run(IReadOnlyList<CodebaseFragment> fragments)
        {
            // M1 — declared nodes: first declarer (input order) wins facts/ProjectName; sites union.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentType declared in fragment.DeclaredTypes)
                DeclareMerged(declared, fragment.ProjectName);

            // External facts table: the first fragment (input order) referencing a not-declared-anywhere
            // FQN wins its facts. Built after M1 so any FQN some fragment declares never becomes external.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentExternal external in fragment.Externals)
                RecordExternal(external);

            // M2 — hierarchy from the winning fragment only; every reference rewired to a merged node.
            foreach ((string fqn, FragmentType declared) in _hierarchy)
                PopulateHierarchy(_nodes[fqn], declared);

            // M3 — edge site-sets union per (src, tgt); self-edge guard; endpoints are merged nodes.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentEdge edge in fragment.Edges)
                MergeEdge(edge);

            // M4 — member-use edge site-sets union per (src, member SymbolId); same-type guard; nodes are merged.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentMemberEdge memberEdge in fragment.MemberEdges)
                MergeMemberEdge(memberEdge);

            // M5 — declared members (GRAMMAR §4.6): the winning fragment's inventory becomes the node's
            // MemberNode list, each member's DeclaringType the same merged node; externals keep their empty
            // default (the member axis is solution-declared-only). Winner-only, exactly like M2 hierarchy.
            foreach ((string fqn, FragmentType declared) in _hierarchy)
                PopulateMembers(_nodes[fqn], declared);

            // M6 — construction edges (GRAMMAR §4.5): site-sets union per (src, constructed); self-construction
            // guard mirrors M3; endpoints are the same merged nodes.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentConstructorEdge constructorEdge in fragment.ConstructorEdges)
                MergeConstructorEdge(constructorEdge);

            // M7 — injection edges (GRAMMAR §4.7): site-sets union per (src, injected); self-injection guard
            // mirrors M3; endpoints are the same merged nodes (an external injected type shares one node).
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentInjectionEdge injectionEdge in fragment.InjectionEdges)
                MergeInjectionEdge(injectionEdge);

            // M8 — registration facts (GRAMMAR §4.7): site-sets union per (lifetime, service, impl?), string-side
            // (no node resolution — registration is many-to-many, resolved model-side at evaluation).
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentServiceRegistration registration in fragment.ServiceRegistrations)
                MergeRegistration(registration);

            // M9 — catch edges (GRAMMAR §4.8): site-sets union per (src, caught); self-catch guard mirrors M3;
            // endpoints are the same merged nodes (an external caught type shares one node).
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentCatchEdge catchEdge in fragment.CatchEdges)
                MergeCatchEdge(catchEdge);

            // M10 — throw edges (GRAMMAR §4.8): site-sets union per (src, thrown); self-throw guard mirrors M3;
            // endpoints are the same merged nodes (an external thrown type shares one node).
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentThrowEdge throwEdge in fragment.ThrowEdges)
                MergeThrowEdge(throwEdge);

            return Materialize(fragments);
        }

        private void DeclareMerged(FragmentType declared, string projectName)
        {
            string fqn = declared.Facts.FullName;
            if (!_nodes.ContainsKey(fqn))
            {
                _nodes[fqn] = NewNode(declared.Facts, projectName, false);
                _hierarchy[fqn] = declared; // the winning (first) declarer supplies the hierarchy
            }
            else
            {
                NoteConflationIfCrossProject(fqn, projectName);
            }

            var sites = DeclarationSites(fqn);
            foreach (FragmentSite site in declared.DeclarationSites) sites.Add(site);
        }

        // A second (or later) declarer of an already-declared FQN. When its project name differs from the
        // winner's, this is same-FQN cross-project conflation: the facts and ProjectName keep following the
        // first declarer, so the loser's copy is invisible to arch.Project selections — record one advisory
        // note. A matching project name is a project's own several target frameworks (M1's legitimate union),
        // which stays silent; the (FQN, loser) dedup collapses a multi-TFM loser to a single note.
        private void NoteConflationIfCrossProject(string fqn, string laterProjectName)
        {
            string winner = _nodes[fqn].ProjectName;
            if (string.Equals(winner, laterProjectName, StringComparison.Ordinal)) return;
            if (!_notedConflations.Add((fqn, laterProjectName))) return;

            _mergeNotes.Add(
                $"Type '{fqn}' is declared by projects '{winner}' and '{laterProjectName}'; its facts and "
                + $"project attribution follow '{winner}' (the first declarer), so arch.Project('{laterProjectName}') "
                + "selections will not include it.");
        }

        private void RecordExternal(FragmentExternal external)
        {
            string fqn = external.Facts.FullName;
            if (!_nodes.ContainsKey(fqn) && !_externalFacts.ContainsKey(fqn)) _externalFacts[fqn] = external;
        }

        private void PopulateHierarchy(TypeNode node, FragmentType declared)
        {
            if (declared.BaseTypeFullName is { } baseFqn) node.BaseType = ResolveNode(baseFqn);

            node.Interfaces = declared.Interfaces
                .Select(f => (ITypeInfo)ResolveNode(f))
                .ToList();

            node.Attributes = declared.Attributes
                .Select(f => (ITypeInfo)ResolveNode(f))
                .ToList();

            node.AllInterfaces = declared.AllInterfaces.Select(ToConstruction).ToList();
            node.BaseTypeChain = declared.BaseTypeChain.Select(ToConstruction).ToList();
            node.AttributeConstructions = declared.AttributeConstructions.Select(ToConstruction).ToList();
        }

        private TypeConstruction ToConstruction(FragmentConstruction construction)
        {
            return new TypeConstruction(ResolveNode(construction.DefinitionFullName), construction.ConstructedName);
        }

        private static void PopulateMembers(TypeNode node, FragmentType declared)
        {
            node.Members = declared.DeclaredMembers
                .Select(member => NewMember(node, member))
                .ToList();
        }

        // The member's declaration sites are already (file, line) ordinal-ordered from extraction, so — as in
        // Materialize's type FilePaths — Distinct preserves first-occurrence file order (the §5.6 contract).
        private static MemberNode NewMember(TypeNode declaringType, FragmentMember member)
        {
            MemberFacts facts = member.Facts;
            return new MemberNode(
                declaringType,
                facts.SymbolId, facts.Name, facts.Kind, facts.Accessibility,
                facts.IsStatic, facts.IsAbstract, facts.IsVirtual, facts.IsAsync,
                facts.ReturnTypeFullName, facts.MemberTypeFullName,
                member.DeclarationSites.Select(s => new SourceLocation(s.File, s.Line)).ToList(),
                member.DeclarationSites.Select(s => s.File).Distinct(StringComparer.Ordinal).ToList());
        }

        private void MergeEdge(FragmentEdge edge)
        {
            if (string.Equals(edge.SourceFullName, edge.TargetFullName, StringComparison.Ordinal)) return; // self-edge

            ResolveNode(edge.SourceFullName);
            ResolveNode(edge.TargetFullName);
            var sites = EdgeSites((edge.SourceFullName, edge.TargetFullName));
            foreach (FragmentSite site in edge.Sites) sites.Add(site);
        }

        private void MergeMemberEdge(FragmentMemberEdge edge)
        {
            // Same-type guard mirrors the edge self-drop; extraction already dropped these, so it is defensive.
            if (string.Equals(edge.SourceFullName, edge.TargetContainingTypeFullName, StringComparison.Ordinal)) return;

            ResolveNode(edge.SourceFullName);
            ResolveNode(edge.TargetContainingTypeFullName);

            // Member facts are functions of the SymbolId, so the first mention wins and every later one agrees.
            _memberFacts.TryAdd(edge.MemberSymbolId, new MemberEdgeFacts(edge.TargetContainingTypeFullName, edge.MemberName, edge.MemberKind));

            var sites = MemberEdgeSites((edge.SourceFullName, edge.MemberSymbolId));
            foreach (FragmentSite site in edge.Sites) sites.Add(site);
        }

        private void MergeConstructorEdge(FragmentConstructorEdge edge)
        {
            // Self-construction guard mirrors the edge self-drop; extraction already dropped these, so it is defensive.
            if (string.Equals(edge.SourceFullName, edge.ConstructedFullName, StringComparison.Ordinal)) return;

            ResolveNode(edge.SourceFullName);
            ResolveNode(edge.ConstructedFullName);
            var sites = ConstructorEdgeSites((edge.SourceFullName, edge.ConstructedFullName));
            foreach (FragmentSite site in edge.Sites) sites.Add(site);
        }

        private void MergeCatchEdge(FragmentCatchEdge edge)
        {
            // Self-catch guard mirrors the edge self-drop; extraction already dropped these, so it is defensive.
            if (string.Equals(edge.SourceFullName, edge.CaughtFullName, StringComparison.Ordinal)) return;

            ResolveNode(edge.SourceFullName);
            ResolveNode(edge.CaughtFullName);
            var sites = CatchEdgeSites((edge.SourceFullName, edge.CaughtFullName));
            foreach (FragmentSite site in edge.Sites) sites.Add(site);
        }

        private void MergeThrowEdge(FragmentThrowEdge edge)
        {
            // Self-throw guard mirrors the edge self-drop; extraction already dropped these, so it is defensive.
            if (string.Equals(edge.SourceFullName, edge.ThrownFullName, StringComparison.Ordinal)) return;

            ResolveNode(edge.SourceFullName);
            ResolveNode(edge.ThrownFullName);
            var sites = ThrowEdgeSites((edge.SourceFullName, edge.ThrownFullName));
            foreach (FragmentSite site in edge.Sites) sites.Add(site);
        }

        private void MergeInjectionEdge(FragmentInjectionEdge edge)
        {
            // Self-injection guard mirrors the edge self-drop; extraction already dropped these, so it is defensive.
            if (string.Equals(edge.SourceFullName, edge.InjectedFullName, StringComparison.Ordinal)) return;

            ResolveNode(edge.SourceFullName);
            ResolveNode(edge.InjectedFullName);
            var sites = InjectionEdgeSites((edge.SourceFullName, edge.InjectedFullName));
            foreach (FragmentSite site in edge.Sites) sites.Add(site);
        }

        // Registrations are string-side (never resolved to nodes): the union key is the whole
        // (lifetime, service FQN, implementation FQN) triple, so two fragments (or two call sites) that name
        // the identical registration collapse to one fact with unioned sites.
        private void MergeRegistration(FragmentServiceRegistration registration)
        {
            var sites = RegistrationSites((registration.Lifetime, registration.ServiceFullName, registration.ImplementationFullName));
            foreach (FragmentSite site in registration.Sites) sites.Add(site);
        }

        /// <summary>
        ///     The declared-anywhere node for <paramref name="fqn" />, else a shallow external node minted
        ///     once from the first-fragment-wins facts table. Every FQN reachable here was recorded as an
        ///     external by whichever fragment references it, so the table always has an entry when the FQN
        ///     is not declared.
        /// </summary>
        private TypeNode ResolveNode(string fqn)
        {
            if (_nodes.TryGetValue(fqn, out TypeNode? node)) return node;

            FragmentExternal external = _externalFacts[fqn];
            node = NewNode(external.Facts, external.AssemblyName, true);
            _nodes[fqn] = node;
            return node;
        }

        private static TypeNode NewNode(TypeFacts facts, string projectName, bool isExternal)
        {
            return new TypeNode(
                facts.FullName, facts.SymbolId, facts.Name, facts.Namespace, facts.Kind,
                facts.Accessibility, facts.IsSealed, facts.IsStatic, facts.IsAbstract, facts.IsRecord,
                projectName, isExternal);
        }

        private CodebaseModel Materialize(IReadOnlyList<CodebaseFragment> fragments)
        {
            foreach ((string fqn, var sites) in _declarationSites)
            {
                TypeNode node = _nodes[fqn];
                node.DeclarationSites = ToLocations(sites);
                // The sites set is already (file, line) ordinal-ordered, so Distinct preserves
                // first-occurrence file order (the GRAMMAR §5.6 FilePaths contract).
                node.FilePaths = sites.Select(s => s.File).Distinct(StringComparer.Ordinal).ToList();
            }

            var types = _nodes.Values
                .OrderBy(n => n.FullName, StringComparer.Ordinal)
                .ToList();

            var edges = _edgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Tgt, StringComparer.Ordinal)
                .Select(kv => new ReferenceEdge(_nodes[kv.Key.Src], _nodes[kv.Key.Tgt], ToLocations(kv.Value)))
                .ToList();

            var memberEdges = BuildMemberEdges();

            var constructorEdges = _constructorEdgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Ctor, StringComparer.Ordinal)
                .Select(kv => new ConstructorEdge(_nodes[kv.Key.Src], _nodes[kv.Key.Ctor], ToLocations(kv.Value)))
                .ToList();

            var injectionEdges = _injectionEdgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Injected, StringComparer.Ordinal)
                .Select(kv => new InjectionEdge(_nodes[kv.Key.Src], _nodes[kv.Key.Injected], ToLocations(kv.Value)))
                .ToList();

            var catchEdges = _catchEdgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Caught, StringComparer.Ordinal)
                .Select(kv => new CatchEdge(_nodes[kv.Key.Src], _nodes[kv.Key.Caught], ToLocations(kv.Value)))
                .ToList();

            var throwEdges = _throwEdgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Thrown, StringComparer.Ordinal)
                .Select(kv => new ThrowEdge(_nodes[kv.Key.Src], _nodes[kv.Key.Thrown], ToLocations(kv.Value)))
                .ToList();

            var serviceRegistrations = _registrationSites
                .OrderBy(kv => kv.Key.Lifetime)
                .ThenBy(kv => kv.Key.Service, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Impl ?? "", StringComparer.Ordinal)
                .Select(kv => new ServiceRegistration(kv.Key.Lifetime, kv.Key.Service, kv.Key.Impl, ToLocations(kv.Value)))
                .ToList();

            // Sort the advisory notes ordinal (they key first on the FQN) so the list is stable across runs
            // regardless of the order distinct conflations were first seen.
            var mergeNotes = _mergeNotes.OrderBy(note => note, StringComparer.Ordinal).ToList();

            return new CodebaseModel(
                types, edges, memberEdges, constructorEdges, injectionEdges, catchEdges, throwEdges,
                serviceRegistrations, BuildProjects(fragments), mergeNotes);
        }

        // Member edges ordered by (source FullName, member SymbolId). A single MemberReference is minted per
        // distinct SymbolId (facts from the first-mention table, containing node from the merged node table), so
        // equal members across edges share the instance.
        private List<MemberEdge> BuildMemberEdges()
        {
            var memberReferences = new Dictionary<string, MemberReference>(StringComparer.Ordinal);

            MemberReference MemberReferenceFor(string symbolId)
            {
                if (memberReferences.TryGetValue(symbolId, out MemberReference? reference)) return reference;

                MemberEdgeFacts facts = _memberFacts[symbolId];
                reference = new MemberReference(_nodes[facts.ContainingFullName], facts.Name, symbolId, facts.Kind);
                memberReferences[symbolId] = reference;
                return reference;
            }

            return _memberEdgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.MemberSymbolId, StringComparer.Ordinal)
                .Select(kv => new MemberEdge(_nodes[kv.Key.Src], MemberReferenceFor(kv.Key.MemberSymbolId), ToLocations(kv.Value)))
                .ToList();
        }

        private static List<ProjectNode> BuildProjects(IReadOnlyList<CodebaseFragment> fragments)
        {
            Dictionary<string, SortedSet<string>> refsByProject = new(StringComparer.Ordinal);
            foreach (CodebaseFragment fragment in fragments)
            {
                if (!refsByProject.TryGetValue(fragment.ProjectName, out var refs))
                {
                    refs = new SortedSet<string>(StringComparer.Ordinal);
                    refsByProject[fragment.ProjectName] = refs;
                }

                foreach (string reference in fragment.ProjectReferences) refs.Add(reference);
            }

            return refsByProject
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new ProjectNode(kv.Key, kv.Value.ToList()))
                .ToList();
        }

        private static IReadOnlyList<SourceLocation> ToLocations(SortedSet<FragmentSite> sites)
        {
            return sites.Select(s => new SourceLocation(s.File, s.Line)).ToList();
        }

        private SortedSet<FragmentSite> DeclarationSites(string fqn)
        {
            if (!_declarationSites.TryGetValue(fqn, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _declarationSites[fqn] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> EdgeSites((string Src, string Tgt) key)
        {
            if (!_edgeSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _edgeSites[key] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> MemberEdgeSites((string Src, string MemberSymbolId) key)
        {
            if (!_memberEdgeSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _memberEdgeSites[key] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> ConstructorEdgeSites((string Src, string Ctor) key)
        {
            if (!_constructorEdgeSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _constructorEdgeSites[key] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> CatchEdgeSites((string Src, string Caught) key)
        {
            if (!_catchEdgeSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _catchEdgeSites[key] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> ThrowEdgeSites((string Src, string Thrown) key)
        {
            if (!_throwEdgeSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _throwEdgeSites[key] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> InjectionEdgeSites((string Src, string Injected) key)
        {
            if (!_injectionEdgeSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _injectionEdgeSites[key] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> RegistrationSites((Lifetime Lifetime, string Service, string? Impl) key)
        {
            if (!_registrationSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _registrationSites[key] = sites;
            }

            return sites;
        }

        /// <summary>
        ///     The merge-side facts a <see cref="MemberReference" /> needs beyond its SymbolId: declaring-type FQN, name,
        ///     kind.
        /// </summary>
        private readonly record struct MemberEdgeFacts(string ContainingFullName, string Name, MemberKind Kind);
    }
}