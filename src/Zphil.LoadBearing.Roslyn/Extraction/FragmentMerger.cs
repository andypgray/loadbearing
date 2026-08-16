using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn.Extraction;

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
///         per shadow.
///     </para>
///     <para>
///         A later declarer under the <em>same</em> project name is one project file's several target
///         frameworks, which union into one project. Their types union too, but a type two frameworks both
///         declare can only carry one framework's facts — the first extracted — so where that actually
///         happens a second kind of merge note records it, one line per project rather than per type. A
///         framework-exclusive type (a <c>#if</c>-guarded class) keeps its own framework's facts and is not
///         what the note is about, so a project whose frameworks share no type stays silent.
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
    ///     references, or nothing for the spec-less survey).
    /// </summary>
    /// <remarks>
    ///     Dropping a project here is byte-identical to never extracting it — a referenced-but-dropped
    ///     project survives as an external node — which is what lets one fragment set serve every caller
    ///     whatever each excludes.
    /// </remarks>
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

        // FQN → the target framework of the fragment that won its facts (null where the framework is unknown,
        // which is every single-framework project and every hand-built fast-path input).
        private readonly Dictionary<string, string?> _declaringFrameworks = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Tgt), SortedSet<FragmentSite>> _edgeSites = new();
        private readonly Dictionary<(string Src, string Exposed), SortedSet<FragmentSite>> _exposureEdgeSites = new();
        private readonly Dictionary<string, FragmentExternal> _externalFacts = new(StringComparer.Ordinal);

        // Project name → every target framework its fragments carried, ordinal-sorted. Built over all
        // fragments, not just the declaring ones, so a note can name the project's whole framework set.
        private readonly Dictionary<string, SortedSet<string>> _frameworksByProject = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FragmentType> _hierarchy = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Injected), SortedSet<FragmentSite>> _injectionEdgeSites = new();
        private readonly Dictionary<(string Src, string MemberSymbolId), SortedSet<FragmentSite>> _memberEdgeSites = new();
        private readonly Dictionary<string, MemberEdgeFacts> _memberFacts = new(StringComparer.Ordinal);

        // Project name → the framework that won the facts of the first type two of the project's frameworks
        // both declared. An entry exists only where such a collapse actually happened, which is the note's
        // gate: without it a note would claim a shared winner for a project whose frameworks share nothing.
        private readonly Dictionary<string, string> _multiFrameworkWinners = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TypeNode> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<(Lifetime Lifetime, string Service, string? Impl), SortedSet<FragmentSite>> _registrationSites = new();
        private readonly Dictionary<(string Src, string Thrown), SortedSet<FragmentSite>> _throwEdgeSites = new();

        public CodebaseModel Run(IReadOnlyList<CodebaseFragment> fragments)
        {
            // The project → target-framework index the multi-framework note reads, indexed before any
            // declaration so a note can name every framework of a project, not only the ones that collapsed.
            foreach (CodebaseFragment fragment in fragments)
                RecordTargetFramework(fragment);

            // Declared nodes: first declarer (input order) wins facts/ProjectName; sites union.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentType declared in fragment.DeclaredTypes)
                DeclareMerged(declared, fragment.ProjectName, fragment.TargetFramework);

            // External facts table: the first fragment (input order) referencing a not-declared-anywhere
            // FQN wins its facts. Built after declaration so an FQN some fragment declares never goes external.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentExternal external in fragment.Externals)
                RecordExternal(external);

            // Hierarchy from the winning fragment only; every reference rewired to a merged node. Declared
            // members (GRAMMAR §4.6) are winner-only in the same way — the winning fragment's inventory
            // becomes the node's MemberNode list — so they are taken in the same pass: the inventory is a
            // function of the fragment and the node alone, and nothing between here and the model reads or
            // writes it. Externals keep their empty default; the member axis is solution-declared-only.
            foreach ((string fqn, FragmentType declared) in _hierarchy)
            {
                TypeNode node = _nodes[fqn];
                PopulateHierarchy(node, declared);
                PopulateMembers(node, declared);
            }

            // Edge site-sets union per (src, tgt).
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentEdge edge in fragment.Edges)
                MergeSimpleEdge(edge.SourceFullName, edge.TargetFullName, _edgeSites, edge.Sites);

            // Member-use edges (GRAMMAR §4.5) key on (src, member SymbolId) rather than a type pair, and the
            // self-drop is therefore a same-type guard; one shared MemberReference per distinct SymbolId.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentMemberEdge memberEdge in fragment.MemberEdges)
                MergeMemberEdge(memberEdge);

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

        private void RecordTargetFramework(CodebaseFragment fragment)
        {
            if (fragment.TargetFramework is not { } targetFramework) return;

            if (!_frameworksByProject.TryGetValue(fragment.ProjectName, out SortedSet<string>? frameworks))
                _frameworksByProject[fragment.ProjectName] = frameworks = new SortedSet<string>(StringComparer.Ordinal);

            frameworks.Add(targetFramework);
        }

        private void DeclareMerged(FragmentType declared, string projectName, string? targetFramework)
        {
            string fqn = declared.Facts.FullName;
            if (!_nodes.ContainsKey(fqn))
            {
                _nodes[fqn] = declared.Facts.ToTypeNode(projectName, isExternal: false);
                _hierarchy[fqn] = declared; // the winning (first) declarer supplies the hierarchy
                _declaringFrameworks[fqn] = targetFramework;
            }
            else
            {
                NoteConflationIfCrossProject(fqn, projectName);
                NoteFrameworkCollapseIfSameProject(fqn, projectName, targetFramework);
            }

            SortedSet<FragmentSite> sites = FragmentSiteSets.For(_declarationSites, fqn);
            foreach (FragmentSite site in declared.DeclarationSites) sites.Add(site);
        }

        // A second (or later) declarer of an already-declared FQN. When its project name differs from the
        // winner's, this is same-FQN cross-project conflation: the facts and ProjectName keep following the
        // first declarer, so the loser's copy is invisible to arch.Project selections — record the loser
        // against the type. A matching project name is a project's own several target frameworks, which the
        // sibling below answers for; the loser set collapses a multi-framework loser to one entry.
        private void NoteConflationIfCrossProject(string fqn, string laterProjectName)
        {
            string winner = _nodes[fqn].ProjectName;
            if (string.Equals(winner, laterProjectName, StringComparison.Ordinal)) return;

            if (!_conflatedLosers.TryGetValue(fqn, out SortedSet<string>? losers))
                _conflatedLosers[fqn] = losers = new SortedSet<string>(StringComparer.Ordinal);

            losers.Add(laterProjectName);
        }

        // The same-project half: one project file's several target frameworks both declaring this FQN, so the
        // type collapses onto whichever framework was extracted first. The three guards are the note's gate,
        // and each is a correctness matter rather than an optimisation:
        //   • a differing project name is cross-project conflation, already recorded above;
        //   • an unknown framework on either side (a single-framework project, a hand-built input) leaves
        //     nothing truthful to say about which framework won;
        //   • a type only ONE framework declares — a #if-guarded class, a framework-conditional <Compile> —
        //     never collapsed at all: its facts follow its own framework, and a note would be false about it.
        private void NoteFrameworkCollapseIfSameProject(string fqn, string laterProjectName, string? laterFramework)
        {
            if (!string.Equals(_nodes[fqn].ProjectName, laterProjectName, StringComparison.Ordinal)) return;
            if (laterFramework is null) return;
            if (!_declaringFrameworks.TryGetValue(fqn, out string? winningFramework) || winningFramework is null) return;
            if (string.Equals(winningFramework, laterFramework, StringComparison.Ordinal)) return;

            // One note per project, so only the first collapse's winner is kept: fragments arrive in
            // (name, framework) order, making it the earliest-extracted framework that actually won a
            // shared type.
            _multiFrameworkWinners.TryAdd(laterProjectName, winningFramework);
        }

        // One note per conflated type, naming every project that loses it. Grouping is what keeps a type
        // stubbed by several sibling projects from spending a line per stub — six types shadowed across six
        // projects cost six lines rather than sixteen, with nothing dropped. The list joiner degrades to
        // "'A' and 'B'" at two items, so a single loser reads as a plain sentence.
        private string ConflationNote(string fqn)
        {
            string winner = _nodes[fqn].ProjectName;
            SortedSet<string> losers = _conflatedLosers[fqn];

            string declarers = JoinWithAnd([$"'{winner}'", .. losers.Select(loser => $"'{loser}'")]);
            string selections = JoinWithAnd([.. losers.Select(loser => $"arch.Project('{loser}')")]);

            return $"Type '{fqn}' is declared by projects {declarers}; its facts and "
                   + $"project attribution follow '{winner}' (the first declarer), so {selections} "
                   + "selections will not include it.";
        }

        // One note per multi-framework project whose frameworks share a type, naming every framework it
        // targets and the one whose facts the shared types carry. Per project rather than per type because
        // the answer is the same for all of them, and a project sharing two hundred types would otherwise
        // drown the channel it is trying to be noticed in.
        private string FrameworkCollapseNote(string projectName)
        {
            SortedSet<string> frameworks = _frameworksByProject[projectName];
            string winner = _multiFrameworkWinners[projectName];
            string targeted = JoinWithAnd([.. frameworks.Select(framework => $"'{framework}'")]);

            return $"Project '{projectName}' targets {targeted}; the types they share take their facts from "
                   + $"'{winner}' (the first extracted), so a rule about them is checked against that "
                   + "framework alone.";
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
            if (!_nodes.ContainsKey(fqn)) _externalFacts.TryAdd(fqn, external);
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
                .Select(member => member.ToMemberNode(node))
                .ToList();
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
            SortedSet<FragmentSite> merged = FragmentSiteSets.For(map, (source, target));
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

            SortedSet<FragmentSite> sites = FragmentSiteSets.For(_memberEdgeSites, (edge.SourceFullName, edge.MemberSymbolId));
            foreach (FragmentSite site in edge.Sites) sites.Add(site);
        }

        private void MergeCatchEdge(FragmentCatchEdge edge)
        {
            if (!MergeSimpleEdge(edge.SourceFullName, edge.CaughtFullName, _catchEdgeSites, edge.Sites)) return;

            // The unfiltered subset unions under the same key and the same guard. Unioning the UNFILTERED sites
            // is what keeps a `#if`-divergent filter honest: a (file, line) filtered in one fragment and
            // unfiltered in another reads unfiltered here, the truthful answer for a ban.
            SortedSet<FragmentSite> unfilteredSites = FragmentSiteSets.For(_catchEdgeUnfilteredSites, (edge.SourceFullName, edge.CaughtFullName));
            foreach (FragmentSite site in edge.UnfilteredSites) unfilteredSites.Add(site);

            // The swallowing subset unions the same way, and for the same reason: a (file, line) that rethrows
            // in one fragment and swallows in another reads swallowing here.
            SortedSet<FragmentSite> swallowingSites = FragmentSiteSets.For(_catchEdgeSwallowingSites, (edge.SourceFullName, edge.CaughtFullName));
            foreach (FragmentSite site in edge.SwallowingSites) swallowingSites.Add(site);
        }

        // Registrations are string-side (never resolved to nodes): the union key is the whole
        // (lifetime, service FQN, implementation FQN) triple, so two fragments (or two call sites) that name
        // the identical registration collapse to one fact with unioned sites.
        private void MergeRegistration(FragmentServiceRegistration registration)
        {
            SortedSet<FragmentSite> sites = FragmentSiteSets.For(
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
            node = external.Facts.ToTypeNode(external.AssemblyName, isExternal: true);
            _nodes[fqn] = node;
            return node;
        }

        private CodebaseModel Materialize(IReadOnlyList<CodebaseFragment> fragments)
        {
            foreach ((string fqn, SortedSet<FragmentSite> sites) in _declarationSites)
            {
                TypeNode node = _nodes[fqn];
                node.DeclarationSites = FragmentSiteSets.Locations(sites);
                node.FilePaths = FragmentSiteSets.FilePaths(sites);
            }

            List<TypeNode> types = _nodes.Values
                .OrderBy(n => n.FullName, StringComparer.Ordinal)
                .ToList();

            List<ReferenceEdge> edges = FragmentSiteSets.OrderedPairs(
                _edgeSites, (src, tgt, sites) => new ReferenceEdge(_nodes[src], _nodes[tgt], FragmentSiteSets.Locations(sites)));

            List<MemberEdge> memberEdges = BuildMemberEdges();

            List<ConstructorEdge> constructorEdges = FragmentSiteSets.OrderedPairs(
                _constructorEdgeSites, (src, ctor, sites) => new ConstructorEdge(_nodes[src], _nodes[ctor], FragmentSiteSets.Locations(sites)));

            List<InjectionEdge> injectionEdges = FragmentSiteSets.OrderedPairs(
                _injectionEdgeSites, (src, injected, sites) => new InjectionEdge(_nodes[src], _nodes[injected], FragmentSiteSets.Locations(sites)));

            // All three site lists come out of SortedSets, so each is (file, line) ordered and each is a subset
            // of the one before it; an edge with no unfiltered (or no swallowing) site materializes the empty list.
            List<CatchEdge> catchEdges = FragmentSiteSets.OrderedPairs(
                _catchEdgeSites,
                (src, caught, sites) => new CatchEdge(
                    _nodes[src], _nodes[caught], FragmentSiteSets.Locations(sites),
                    _catchEdgeUnfilteredSites.TryGetValue((src, caught), out SortedSet<FragmentSite>? unfiltered) ? FragmentSiteSets.Locations(unfiltered) : [],
                    _catchEdgeSwallowingSites.TryGetValue((src, caught), out SortedSet<FragmentSite>? swallowing) ? FragmentSiteSets.Locations(swallowing) : []));

            List<ThrowEdge> throwEdges = FragmentSiteSets.OrderedPairs(
                _throwEdgeSites, (src, thrown, sites) => new ThrowEdge(_nodes[src], _nodes[thrown], FragmentSiteSets.Locations(sites)));

            List<ExposureEdge> exposureEdges = FragmentSiteSets.OrderedPairs(
                _exposureEdgeSites, (src, exposed, sites) => new ExposureEdge(_nodes[src], _nodes[exposed], FragmentSiteSets.Locations(sites)));

            List<ServiceRegistration> serviceRegistrations = FragmentSiteSets.OrderedRegistrations(
                _registrationSites,
                (lifetime, service, impl, sites) => new ServiceRegistration(
                    lifetime, service, impl, FragmentSiteSets.Locations(sites)));

            // The advisory notes: project-level first (ordinal by project), then per-type (ordinal by FQN) —
            // coarse fact before fine, and each half sorted on the key it groups by, so the list is stable
            // across runs regardless of the order the distinct notes were first raised.
            IEnumerable<string> frameworkCollapseNotes = _multiFrameworkWinners.Keys
                .OrderBy(projectName => projectName, StringComparer.Ordinal)
                .Select(FrameworkCollapseNote);
            IEnumerable<string> conflationNotes = _conflatedLosers.Keys
                .OrderBy(fqn => fqn, StringComparer.Ordinal)
                .Select(ConflationNote);
            List<string> mergeNotes = frameworkCollapseNotes
                .Concat(conflationNotes)
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
                (src, symbolId, sites) => new MemberEdge(_nodes[src], MemberReferenceFor(symbolId), FragmentSiteSets.Locations(sites)));
        }

        private static List<ProjectNode> BuildProjects(IReadOnlyList<CodebaseFragment> fragments)
        {
            Dictionary<string, SortedSet<string>> refsByProject = new(StringComparer.Ordinal);
            Dictionary<string, bool?> memberByProject = new(StringComparer.Ordinal);
            foreach (CodebaseFragment fragment in fragments)
            {
                if (!refsByProject.TryGetValue(fragment.ProjectName, out SortedSet<string>? refs))
                {
                    refs = new SortedSet<string>(StringComparer.Ordinal);
                    refsByProject[fragment.ProjectName] = refs;
                }

                foreach (string reference in fragment.ProjectReferences) refs.Add(reference);

                memberByProject[fragment.ProjectName] = UnionMembership(
                    memberByProject.GetValueOrDefault(fragment.ProjectName), fragment.SolutionMember);
            }

            return refsByProject
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new ProjectNode(kv.Key, kv.Value.ToList(), memberByProject[kv.Key]))
                .ToList();
        }

        // A multi-targeted project arrives as one fragment per framework under the one name the load boundary
        // normalized them to, so its membership unions the way its reference edges do: declared by any
        // fragment is declared. Unknown loses to either verdict — a fragment that read nothing has nothing to
        // contradict one that did — so the result is null only when every fragment was unlabeled, which is the
        // fail-open answer the whole thread degrades to.
        private static bool? UnionMembership(bool? left, bool? right)
        {
            if (left is null) return right;
            if (right is null) return left;

            return left.Value || right.Value;
        }

        /// <summary>
        ///     The merge-side facts a <see cref="MemberReference" /> needs beyond its SymbolId: declaring-type FQN, name,
        ///     kind.
        /// </summary>
        private readonly record struct MemberEdgeFacts(string ContainingFullName, string Name, MemberKind Kind);
    }
}
