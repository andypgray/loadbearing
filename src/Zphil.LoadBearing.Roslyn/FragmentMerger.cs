using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Merges a set of per-input <see cref="CodebaseFragment" />s into one <see cref="CodebaseModel" />,
///     unifying by fully-qualified name so the model carries global cross-input semantics rather than one
///     compilation's view. One code path serves cold runs, the fast test path, and cache hits, so the
///     cache cannot change results by construction.
/// </summary>
/// <remarks>
///     <para>
///         Two rules run across every axis, and each step below states only how its own axis instantiates
///         them. Per-type <b>facts</b> — a node's shape and <c>ProjectName</c>, its hierarchy, its member
///         inventory — are <em>winner-only</em>: fragments are visited in input (ordinal-project) order and
///         the first declarer wins, so a later fragment never overwrites what an earlier one established.
///         Per-edge <b>evidence</b> is <em>unioned</em>: each axis keys its site-set on the edge's endpoint
///         pair, drops the self-edge, and resolves both endpoints to the same <see cref="TypeNode" />
///         instances <see cref="CodebaseModel.Types" /> holds — so an external endpoint is one shared node
///         however many fragments referenced it.
///     </para>
///     <para>
///         Declaring the nodes fully precedes populating their hierarchy, and that ordering is
///         load-bearing: it is what makes a type any fragment declares beat every fragment's external view
///         of it, so <c>ResolveNode</c> mints a shallow external only for an FQN no fragment declares at all.
///     </para>
///     <para>
///         A later declarer under a <em>different</em> project name is same-FQN cross-project conflation.
///         Facts still follow the first declarer, and an advisory
///         <see cref="CodebaseModel.MergeNotes">merge note</see> records it — one note per conflated FQN
///         naming every losing project, so a type several projects shadow costs one line rather than one
///         per shadow. A project's own several target frameworks share its name, so they union silently.
///     </para>
///     <para>
///         Where one external FQN carries genuinely different facts across compilations (mixed assembly
///         versions), the winner is the first fragment in input order that <em>references</em> it. With a
///         uniform reference closure — the norm — the facts are identical whichever fragment wins, so no
///         model observable distinguishes them; the rule exists to make the tie deterministic rather than
///         dictionary-iteration order.
///     </para>
/// </remarks>
internal static class FragmentMerger
{
    public static CodebaseModel Merge(IReadOnlyList<CodebaseFragment> fragments)
    {
        return new MergeState().Run(fragments);
    }

    /// <summary>
    ///     The merge inputs a caller keeps: <paramref name="fragments" /> minus
    ///     <paramref name="excludeProjectNames" /> (its spec project and the private plumbing only that
    ///     references, or nothing for the spec-less survey). Dropping a project here is byte-identical to
    ///     never extracting it — a referenced-but-dropped project survives as an external node — which is
    ///     what lets one fragment set serve every caller whatever each excludes.
    /// </summary>
    internal static List<CodebaseFragment> Retain(
        IReadOnlyList<CodebaseFragment> fragments, IReadOnlyCollection<string> excludeProjectNames)
    {
        if (excludeProjectNames.Count == 0) return fragments.ToList();

        var excluded = new HashSet<string>(excludeProjectNames, StringComparer.Ordinal);
        return fragments.Where(f => !excluded.Contains(f.ProjectName)).ToList();
    }

    private sealed class MergeState
    {
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeSites = new();
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeSwallowingSites = new();
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeUnfilteredSites = new();

        // Conflated FQN → every project that declared it after the winner, ordinal-sorted. One entry per
        // type, not per (type, loser) pair, so the advisory note can name all the losers in one line.
        private readonly Dictionary<string, SortedSet<string>> _conflatedLosers = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Ctor), SortedSet<FragmentSite>> _constructorEdgeSites = new();

        private readonly Dictionary<string, SortedSet<FragmentSite>> _declarationSites = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Tgt), SortedSet<FragmentSite>> _edgeSites = new();
        private readonly Dictionary<(string Src, string Exposed), SortedSet<FragmentSite>> _exposureEdgeSites = new();
        private readonly Dictionary<string, FragmentExternal> _externalFacts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FragmentType> _hierarchy = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Injected), SortedSet<FragmentSite>> _injectionEdgeSites = new();
        private readonly Dictionary<(string Src, string MemberSymbolId), SortedSet<FragmentSite>> _memberEdgeSites = new();
        private readonly Dictionary<string, MemberEdgeFacts> _memberFacts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TypeNode> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<(Lifetime Lifetime, string Service, string? Impl), SortedSet<FragmentSite>> _registrationSites = new();
        private readonly Dictionary<(string Src, string Thrown), SortedSet<FragmentSite>> _throwEdgeSites = new();

        public CodebaseModel Run(IReadOnlyList<CodebaseFragment> fragments)
        {
            // Declared nodes: first declarer (input order) wins facts/ProjectName; sites union.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentType declared in fragment.DeclaredTypes)
                DeclareMerged(declared, fragment.ProjectName);

            // External facts table: the first fragment (input order) referencing a not-declared-anywhere
            // FQN wins its facts. Built after declaration so an FQN some fragment declares never goes external.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentExternal external in fragment.Externals)
                RecordExternal(external);

            // Hierarchy from the winning fragment only; every reference rewired to a merged node.
            foreach ((string fqn, FragmentType declared) in _hierarchy)
                PopulateHierarchy(_nodes[fqn], declared);

            // Edge site-sets union per (src, tgt).
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentEdge edge in fragment.Edges)
                MergeSimpleEdge(edge.SourceFullName, edge.TargetFullName, _edgeSites, edge.Sites);

            // Member-use edges (GRAMMAR §4.5) key on (src, member SymbolId) rather than a type pair, and the
            // self-drop is therefore a same-type guard; one shared MemberReference per distinct SymbolId.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentMemberEdge memberEdge in fragment.MemberEdges)
                MergeMemberEdge(memberEdge);

            // Declared members (GRAMMAR §4.6) are winner-only like the hierarchy: the winning fragment's
            // inventory becomes the node's MemberNode list, each member's DeclaringType the same merged node.
            // Externals keep their empty default — the member axis is solution-declared-only.
            foreach ((string fqn, FragmentType declared) in _hierarchy)
                PopulateMembers(_nodes[fqn], declared);

            // Construction edges (GRAMMAR §4.5) key on (src, constructed).
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentConstructorEdge constructorEdge in fragment.ConstructorEdges)
                MergeSimpleEdge(
                    constructorEdge.SourceFullName, constructorEdge.ConstructedFullName, _constructorEdgeSites, constructorEdge.Sites);

            // Injection edges (GRAMMAR §4.7) key on (src, injected).
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentInjectionEdge injectionEdge in fragment.InjectionEdges)
                MergeSimpleEdge(
                    injectionEdge.SourceFullName, injectionEdge.InjectedFullName, _injectionEdgeSites, injectionEdge.Sites);

            // Registration facts (GRAMMAR §4.7) are the one axis with no node resolution: they key on
            // (lifetime, service, impl?) string-side, because registration is many-to-many and membership is
            // resolved model-side at evaluation.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentServiceRegistration registration in fragment.ServiceRegistrations)
                MergeRegistration(registration);

            // Catch edges (GRAMMAR §4.8) key on (src, caught), and their unfiltered- and swallowing-site
            // subsets union under that same key and guard — so a file:line any fragment reports unfiltered
            // (or swallowing) keeps that standing in the merged edge.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentCatchEdge catchEdge in fragment.CatchEdges)
                MergeCatchEdge(catchEdge);

            // Throw edges (GRAMMAR §4.8) key on (src, thrown).
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentThrowEdge throwEdge in fragment.ThrowEdges)
                MergeSimpleEdge(throwEdge.SourceFullName, throwEdge.ThrownFullName, _throwEdgeSites, throwEdge.Sites);

            // Exposure edges (GRAMMAR §4.9) key on (src, exposed).
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentExposureEdge exposureEdge in fragment.ExposureEdges)
                MergeSimpleEdge(
                    exposureEdge.SourceFullName, exposureEdge.ExposedFullName, _exposureEdgeSites, exposureEdge.Sites);

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

            var sites = FragmentSiteSets.For(_declarationSites, fqn);
            foreach (FragmentSite site in declared.DeclarationSites) sites.Add(site);
        }

        // A second (or later) declarer of an already-declared FQN. When its project name differs from the
        // winner's, this is same-FQN cross-project conflation: the facts and ProjectName keep following the
        // first declarer, so the loser's copy is invisible to arch.Project selections — record the loser
        // against the type. A matching project name is a project's own several target frameworks — a
        // legitimate union, which stays silent; the loser set collapses a multi-TFM loser to one entry.
        private void NoteConflationIfCrossProject(string fqn, string laterProjectName)
        {
            string winner = _nodes[fqn].ProjectName;
            if (string.Equals(winner, laterProjectName, StringComparison.Ordinal)) return;

            if (!_conflatedLosers.TryGetValue(fqn, out var losers))
                _conflatedLosers[fqn] = losers = new SortedSet<string>(StringComparer.Ordinal);

            losers.Add(laterProjectName);
        }

        // One note per conflated type, naming every project that loses it. Grouping is what keeps a type
        // stubbed by several sibling projects from spending a line per stub — six types shadowed across six
        // projects cost six lines rather than sixteen, with nothing dropped. The list joiner degrades to
        // "'A' and 'B'" at two items, so a single loser reads as a plain sentence.
        private string ConflationNote(string fqn)
        {
            string winner = _nodes[fqn].ProjectName;
            var losers = _conflatedLosers[fqn];

            string declarers = JoinWithAnd([$"'{winner}'", .. losers.Select(loser => $"'{loser}'")]);
            string selections = JoinWithAnd([.. losers.Select(loser => $"arch.Project('{loser}')")]);

            return $"Type '{fqn}' is declared by projects {declarers}; its facts and "
                   + $"project attribution follow '{winner}' (the first declarer), so {selections} "
                   + "selections will not include it.";
        }

        // "A", "A and B", "A, B and C" — each caller formats its own items, so this only joins.
        private static string JoinWithAnd(IReadOnlyList<string> items)
        {
            if (items.Count == 1) return items[0];

            return string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
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
        // Materialize's type FilePaths — Distinct preserves first-occurrence file order (the §5.6 contract). The
        // parameter facts are already in declaration order and the attribute facts ordinal by constructed name,
        // both winner-only (the winning fragment's inventory is taken whole), so each Select preserves order and
        // no merge path duplicates or reorders them. Unlike the type-side attribute list, the member's stays
        // string-side: no ResolveNode, so an attribute only a member wears mints no external node.
        private static MemberNode NewMember(TypeNode declaringType, FragmentMember member)
        {
            MemberFacts facts = member.Facts;
            return new MemberNode(
                declaringType,
                facts.SymbolId, facts.Name, facts.Kind, facts.Accessibility,
                facts.IsStatic, facts.IsAbstract, facts.IsVirtual, facts.IsAsync,
                facts.ReturnTypeFullName, facts.MemberTypeFullName,
                member.DeclarationSites.Select(s => new SourceLocation(s.File, s.Line)).ToList(),
                member.DeclarationSites.Select(s => s.File).Distinct(StringComparer.Ordinal).ToList(),
                facts.Parameters.Select(p => new ParameterNode(p.Name, p.TypeFullName)).ToList(),
                facts.Attributes.Select(a => new AttributeNode(a.DefinitionFullName, a.ConstructedName)).ToList());
        }

        // The one edge merge, stated once for every axis that keys on an endpoint pair: the ordinal self-edge
        // guard (extraction already dropped these, so it is defensive), ResolveNode on BOTH endpoints — the
        // rule that keeps an external endpoint one shared node however many fragments referenced it — and the
        // site union. Returns false for the dropped self-edge, so an axis carrying parallel subsets (catch)
        // gates them on the same guard rather than restating it.
        private bool MergeSimpleEdge(
            string source,
            string target,
            Dictionary<(string, string), SortedSet<FragmentSite>> map,
            IReadOnlyList<FragmentSite> sites)
        {
            if (string.Equals(source, target, StringComparison.Ordinal)) return false;

            ResolveNode(source);
            ResolveNode(target);
            var merged = FragmentSiteSets.For(map, (source, target));
            foreach (FragmentSite site in sites) merged.Add(site);
            return true;
        }

        private void MergeMemberEdge(FragmentMemberEdge edge)
        {
            // Same-type guard mirrors the edge self-drop; extraction already dropped these, so it is defensive.
            if (string.Equals(edge.SourceFullName, edge.TargetContainingTypeFullName, StringComparison.Ordinal)) return;

            ResolveNode(edge.SourceFullName);
            ResolveNode(edge.TargetContainingTypeFullName);

            // Member facts are functions of the SymbolId, so the first mention wins and every later one agrees.
            _memberFacts.TryAdd(edge.MemberSymbolId, new MemberEdgeFacts(edge.TargetContainingTypeFullName, edge.MemberName, edge.MemberKind));

            var sites = FragmentSiteSets.For(_memberEdgeSites, (edge.SourceFullName, edge.MemberSymbolId));
            foreach (FragmentSite site in edge.Sites) sites.Add(site);
        }

        private void MergeCatchEdge(FragmentCatchEdge edge)
        {
            if (!MergeSimpleEdge(edge.SourceFullName, edge.CaughtFullName, _catchEdgeSites, edge.Sites)) return;

            // The unfiltered subset unions under the same key and the same guard. Unioning the UNFILTERED sites
            // is what keeps a `#if`-divergent filter honest: a (file, line) filtered in one fragment and
            // unfiltered in another reads unfiltered here, the truthful answer for a ban.
            var unfilteredSites = FragmentSiteSets.For(_catchEdgeUnfilteredSites, (edge.SourceFullName, edge.CaughtFullName));
            foreach (FragmentSite site in edge.UnfilteredSites) unfilteredSites.Add(site);

            // The swallowing subset unions the same way, and for the same reason: a (file, line) that rethrows
            // in one fragment and swallows in another reads swallowing here.
            var swallowingSites = FragmentSiteSets.For(_catchEdgeSwallowingSites, (edge.SourceFullName, edge.CaughtFullName));
            foreach (FragmentSite site in edge.SwallowingSites) swallowingSites.Add(site);
        }

        // Registrations are string-side (never resolved to nodes): the union key is the whole
        // (lifetime, service FQN, implementation FQN) triple, so two fragments (or two call sites) that name
        // the identical registration collapse to one fact with unioned sites.
        private void MergeRegistration(FragmentServiceRegistration registration)
        {
            var sites = FragmentSiteSets.For(
                _registrationSites, (registration.Lifetime, registration.ServiceFullName, registration.ImplementationFullName));
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
                facts.IsGenerated, projectName, isExternal);
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

            var edges = FragmentSiteSets.OrderedPairs(
                _edgeSites, (src, tgt, sites) => new ReferenceEdge(_nodes[src], _nodes[tgt], ToLocations(sites)));

            var memberEdges = BuildMemberEdges();

            var constructorEdges = FragmentSiteSets.OrderedPairs(
                _constructorEdgeSites, (src, ctor, sites) => new ConstructorEdge(_nodes[src], _nodes[ctor], ToLocations(sites)));

            var injectionEdges = FragmentSiteSets.OrderedPairs(
                _injectionEdgeSites, (src, injected, sites) => new InjectionEdge(_nodes[src], _nodes[injected], ToLocations(sites)));

            // All three site lists come out of SortedSets, so each is (file, line) ordered and each is a subset
            // of the one before it; an edge with no unfiltered (or no swallowing) site materializes the empty list.
            var catchEdges = FragmentSiteSets.OrderedPairs(
                _catchEdgeSites,
                (src, caught, sites) => new CatchEdge(
                    _nodes[src], _nodes[caught], ToLocations(sites),
                    _catchEdgeUnfilteredSites.TryGetValue((src, caught), out var unfiltered) ? ToLocations(unfiltered) : [],
                    _catchEdgeSwallowingSites.TryGetValue((src, caught), out var swallowing) ? ToLocations(swallowing) : []));

            var throwEdges = FragmentSiteSets.OrderedPairs(
                _throwEdgeSites, (src, thrown, sites) => new ThrowEdge(_nodes[src], _nodes[thrown], ToLocations(sites)));

            var exposureEdges = FragmentSiteSets.OrderedPairs(
                _exposureEdgeSites, (src, exposed, sites) => new ExposureEdge(_nodes[src], _nodes[exposed], ToLocations(sites)));

            var serviceRegistrations = _registrationSites
                .OrderBy(kv => kv.Key.Lifetime)
                .ThenBy(kv => kv.Key.Service, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Impl ?? "", StringComparer.Ordinal)
                .Select(kv => new ServiceRegistration(kv.Key.Lifetime, kv.Key.Service, kv.Key.Impl, ToLocations(kv.Value)))
                .ToList();

            // Sort the advisory notes by the FQN they key on, so the list is stable across runs regardless
            // of the order distinct conflations were first seen.
            var mergeNotes = _conflatedLosers.Keys
                .OrderBy(fqn => fqn, StringComparer.Ordinal)
                .Select(ConflationNote)
                .ToList();

            return new CodebaseModel(
                types, edges, memberEdges, constructorEdges, injectionEdges, catchEdges, throwEdges,
                exposureEdges, serviceRegistrations, BuildProjects(fragments), mergeNotes);
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

            return FragmentSiteSets.OrderedPairs(
                _memberEdgeSites,
                (src, symbolId, sites) => new MemberEdge(_nodes[src], MemberReferenceFor(symbolId), ToLocations(sites)));
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

        /// <summary>
        ///     The merge-side facts a <see cref="MemberReference" /> needs beyond its SymbolId: declaring-type FQN, name,
        ///     kind.
        /// </summary>
        private readonly record struct MemberEdgeFacts(string ContainingFullName, string Name, MemberKind Kind);
    }
}
