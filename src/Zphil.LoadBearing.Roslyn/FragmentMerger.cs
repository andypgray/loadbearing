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
///             projects, or a project's several target frameworks).
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
///     </list>
///     One code path serves cold runs, the fast test path, and (Phase 11 WP6) cache hits, so the cache
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
        private readonly Dictionary<string, SortedSet<FragmentSite>> _declarationSites = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Tgt), SortedSet<FragmentSite>> _edgeSites = new();
        private readonly Dictionary<string, FragmentExternal> _externalFacts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FragmentType> _hierarchy = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TypeNode> _nodes = new(StringComparer.Ordinal);

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

            var sites = DeclarationSites(fqn);
            foreach (FragmentSite site in declared.DeclarationSites) sites.Add(site);
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

        private void MergeEdge(FragmentEdge edge)
        {
            if (string.Equals(edge.SourceFullName, edge.TargetFullName, StringComparison.Ordinal)) return; // self-edge

            ResolveNode(edge.SourceFullName);
            ResolveNode(edge.TargetFullName);
            var sites = EdgeSites((edge.SourceFullName, edge.TargetFullName));
            foreach (FragmentSite site in edge.Sites) sites.Add(site);
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

            return new CodebaseModel(types, edges, BuildProjects(fragments));
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
    }
}