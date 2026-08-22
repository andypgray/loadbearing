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
///         of it — with one qualification, which is the whole of the rule below. A fragment's external view
///         is only <em>of</em> that declaration when the assembly it bound the name from is one the declaring
///         fragments produce. Where it is not, the two are different types wearing one name.
///     </para>
///     <para>
///         So a fully-qualified name can denote two nodes: the source declaration, and a shallow external
///         attributed to the supplying assembly, where the assembly a fragment bound the name from is one
///         no declaring fragment produces. Each fragment's endpoints resolve to whichever its compilation
///         actually bound, on every axis including the hierarchy, and the split is disclosed as an advisory
///         <see cref="CodebaseModel.MergeNotes">merge note</see> and again in the survey. Internally every
///         table pairing endpoints keys on a <c>NodeKey</c> — the name, plus the supplying assembly where
///         it is the shadow — never on the name alone: a table indexed by node is not keyed by
///         <see langword="string" />, so crossing between the two key spaces has to be spelled and a missed
///         crossing does not compile.
///     </para>
///     <para>
///         A later declarer under a <em>different</em> project name is same-FQN cross-project conflation:
///         facts still follow the first declarer, recorded as a merge note and as
///         <see cref="TypeNode.AlsoDeclaredBy" /> on the winning node — the queryable form a consumer that
///         must act on the conflation reads instead of the prose.
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

    /// <summary>
    ///     <paramref name="excludeProjectNames" /> as one ordinal-stable string, for a caller memoizing the
    ///     models it merges from one fragment set. Two callers excluding the same projects in a different
    ///     order are the same merge, so the key sorts; <c>'\n'</c> cannot occur in a project name.
    /// </summary>
    /// <remarks>
    ///     It lives beside <see cref="Retain" /> because the two have to agree: the key identifies a merge
    ///     exactly as far as <c>Retain</c> distinguishes one, and both memos in the product — the warm
    ///     session store's and the per-run source's — would strand a wrong model if they diverged.
    /// </remarks>
    internal static string ExclusionKey(IReadOnlyCollection<string> excludeProjectNames)
    {
        if (excludeProjectNames.Count == 0) return "";

        return string.Join("\n", excludeProjectNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    private sealed class MergeState
    {
        // The supplier map every fragment shares when nothing is shadowed. NodeKeyFor asks the shadow table
        // before it asks a supplier map, so with no shadowed name there is no lookup left to answer and one
        // map serves the whole merge. Never written to.
        private static readonly Dictionary<string, string> EmptySuppliers = new(StringComparer.Ordinal);

        private readonly Dictionary<(NodeKey Src, NodeKey Caught), SortedSet<FragmentSite>> _catchEdgeSites = new();
        private readonly Dictionary<(NodeKey Src, NodeKey Caught), SortedSet<FragmentSite>> _catchEdgeSwallowingSites = new();
        private readonly Dictionary<(NodeKey Src, NodeKey Caught), SortedSet<FragmentSite>> _catchEdgeUnfilteredSites = new();

        // Conflated FQN → every project that declared it after the winner, ordinal-sorted. One entry per
        // type, not per (type, loser) pair, so the advisory note can name all the losers in one line.
        private readonly Dictionary<string, SortedSet<string>> _conflatedLosers = new(StringComparer.Ordinal);
        private readonly Dictionary<(NodeKey Src, NodeKey Ctor), SortedSet<FragmentSite>> _constructorEdgeSites = new();

        private readonly Dictionary<string, SortedSet<FragmentSite>> _declarationSites = new(StringComparer.Ordinal);

        // FQN → the target framework of the fragment that won its facts (null where the framework is unknown,
        // which is every single-framework project and every hand-built fast-path input).
        private readonly Dictionary<string, string?> _declaringFrameworks = new(StringComparer.Ordinal);
        private readonly Dictionary<(NodeKey Src, NodeKey Tgt), SortedSet<FragmentSite>> _edgeSites = new();
        private readonly Dictionary<(NodeKey Src, NodeKey Exposed), SortedSet<FragmentSite>> _exposureEdgeSites = new();
        private readonly Dictionary<NodeKey, FragmentExternal> _externalFacts = new();

        // Project name → every target framework its fragments carried, ordinal-sorted. Built over all
        // fragments, not just the declaring ones, so a note can name the project's whole framework set.
        private readonly Dictionary<string, SortedSet<string>> _frameworksByProject = new(StringComparer.Ordinal);

        // FQN → every assembly name a fragment declaring it was compiled into, and the index of the
        // fragment whose declaration won its facts. The first is the declaration half of the shadow test —
        // EVERY declarer, not just the winner, because a name one project wins on ordinal order can be
        // bound from a sibling project that declares it too, and that is an ordinary intra-solution
        // reference rather than a shadow. An unknown assembly name contributes nothing, so a set that
        // stays empty answers "unknown" and the test fails open.
        private readonly Dictionary<string, HashSet<string>> _declaringAssemblies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FragmentType> _hierarchy = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _hierarchyFragments = new(StringComparer.Ordinal);
        private readonly Dictionary<(NodeKey Src, NodeKey Injected), SortedSet<FragmentSite>> _injectionEdgeSites = new();
        private readonly Dictionary<(NodeKey Src, string MemberSymbolId), SortedSet<FragmentSite>> _memberEdgeSites = new();
        private readonly Dictionary<string, MemberEdgeFacts> _memberFacts = new(StringComparer.Ordinal);

        // Project name → the framework that won the facts of the first type two of the project's frameworks
        // both declared. An entry exists only where such a collapse actually happened, which is the note's
        // gate: without it a note would claim a shared winner for a project whose frameworks share nothing.
        private readonly Dictionary<string, string> _multiFrameworkWinners = new(StringComparer.Ordinal);
        private readonly Dictionary<NodeKey, TypeNode> _nodes = new();
        private readonly Dictionary<(Lifetime Lifetime, string Service, string? Impl), SortedSet<FragmentSite>> _registrationSites = new();

        // Shadowed FQN → every project whose own compilation bound that name from a shadowing assembly
        // rather than from the declaration, ordinal-sorted. Recorded where the answer is already in hand —
        // the supplier pass walks exactly these externals — rather than re-derived from the edges, which can
        // only see the names an edge was minted for: a name reached solely as a member's parameter or return
        // type mints none at all (GRAMMAR §4.6), and a bare `catch` or a `throw expr;` spells no type name to
        // mint one from. Written only from that pass, so a solution with no shadowed name builds none.
        private readonly Dictionary<string, SortedSet<string>> _shadowBinders = new(StringComparer.Ordinal);

        // Shadowed FQN → every assembly that supplied that name to a fragment which did NOT compile it and
        // which none of its declarers produce, ordinal-sorted. One entry per type rather than per
        // (type, assembly) pair, the grouping _conflatedLosers uses, so one line can name them all. This
        // table is both the shadow node's gate and the merge note's content: a split the report does not
        // disclose is untypeable.
        private readonly Dictionary<string, SortedSet<string>> _shadowingAssemblies = new(StringComparer.Ordinal);
        private readonly Dictionary<(NodeKey Src, NodeKey Thrown), SortedSet<FragmentSite>> _throwEdgeSites = new();

        public CodebaseModel Run(IReadOnlyList<CodebaseFragment> fragments)
        {
            // The project → target-framework index the multi-framework note reads, indexed before any
            // declaration so a note can name every framework of a project, not only the ones that collapsed.
            foreach (CodebaseFragment fragment in fragments)
                RecordTargetFramework(fragment);

            // Declared nodes: first declarer (input order) wins facts/ProjectName; sites union. The index
            // rides along because the winner's own bindings are what its hierarchy is later resolved through.
            for (var index = 0; index < fragments.Count; index++)
            {
                CodebaseFragment fragment = fragments[index];
                foreach (FragmentType declared in fragment.DeclaredTypes)
                    DeclareMerged(declared, fragment, index);
            }

            // External facts table: the first fragment (input order) referencing a not-declared-anywhere
            // FQN wins its facts. Built after declaration so the pass below can ask "does anything declare
            // this?" of _nodes alone, which is what splits an ordinary external from a shadowed name.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentExternal external in fragment.Externals)
                RecordExternal(external);

            // Every shadow gets its node here rather than waiting to be resolved by an endpoint, so the
            // report and the model cannot disagree: a name is noted as meaning two types exactly when Types
            // carries two. Without this a name referenced only where extraction keeps its facts as strings —
            // a member's parameter or return type (GRAMMAR §4.6) — would be noted and then not be there.
            foreach ((string fqn, SortedSet<string> shadowing) in _shadowingAssemblies)
            foreach (string assembly in shadowing)
                ResolveNode(new NodeKey(fqn, assembly));

            // Each fragment's own answer to "which assembly did I bind this name from", built once here and
            // read by every pass below. It has to come after the two passes above, because a supplier is
            // only interesting once the tables know which names are declared and which of those are shadowed.
            // That is also the whole cost model: a map is consulted for a shadowed name and for nothing else,
            // so a solution with no shadow — which is nearly every solution — consults none and builds none,
            // and one with a shadow records the handful of names in question rather than every external.
            List<Dictionary<string, string>> suppliers = _shadowingAssemblies.Count == 0
                ? Enumerable.Repeat(EmptySuppliers, fragments.Count)
                    .ToList()
                : fragments.Select(SuppliersOf)
                    .ToList();

            // Hierarchy from the winning fragment only, resolved through THAT fragment's bindings — a
            // declarer whose own compilation bound a shadowed name from an assembly reaches the assembly's
            // node, exactly as its edges do. Declared members (GRAMMAR §4.6) are winner-only in the same way
            // — the winning fragment's inventory becomes the node's MemberNode list — so they are taken in
            // the same pass: the inventory is a function of the fragment and the node alone, and nothing
            // between here and the model reads or writes it. Externals keep their empty default; the member
            // axis is solution-declared-only.
            foreach ((string fqn, FragmentType declared) in _hierarchy)
            {
                TypeNode node = _nodes[NodeKey.Unshadowed(fqn)];
                PopulateHierarchy(node, declared, suppliers[_hierarchyFragments[fqn]]);
                PopulateMembers(node, declared);
            }

            // Every edge family, one fragment at a time rather than one family at a time, so each family
            // reads the supplier index of the fragment that observed it. Interleaving the families changes
            // only the order the work runs in, and nothing here is order-sensitive: every site set is a
            // SortedSet, every map keys on its endpoint pair, the external facts table was frozen in the
            // pass above so ResolveNode mints the same facts whenever it first runs, _memberFacts is
            // first-wins over facts that are a function of the SymbolId, and Materialize re-sorts the nodes.
            for (var index = 0; index < fragments.Count; index++)
            {
                CodebaseFragment fragment = fragments[index];
                Dictionary<string, string> fragmentSuppliers = suppliers[index];

                // Edge site-sets union per (src, tgt).
                foreach (FragmentEdge edge in fragment.Edges)
                    MergeSimpleEdge(fragmentSuppliers, edge.SourceFullName, edge.TargetFullName, _edgeSites, edge.Sites);

                // Member-use edges (GRAMMAR §4.5) key on (src, member SymbolId) rather than a type pair, and
                // the self-drop is therefore a same-type guard; one shared MemberReference per distinct SymbolId.
                foreach (FragmentMemberEdge memberEdge in fragment.MemberEdges)
                    MergeMemberEdge(fragmentSuppliers, memberEdge);

                // Construction edges (GRAMMAR §4.5) key on (src, constructed).
                foreach (FragmentConstructorEdge constructorEdge in fragment.ConstructorEdges)
                    MergeSimpleEdge(
                        fragmentSuppliers, constructorEdge.SourceFullName, constructorEdge.ConstructedFullName,
                        _constructorEdgeSites, constructorEdge.Sites);

                // Injection edges (GRAMMAR §4.7) key on (src, injected).
                foreach (FragmentInjectionEdge injectionEdge in fragment.InjectionEdges)
                    MergeSimpleEdge(
                        fragmentSuppliers, injectionEdge.SourceFullName, injectionEdge.InjectedFullName,
                        _injectionEdgeSites, injectionEdge.Sites);

                // Registration facts (GRAMMAR §4.7) are the one axis with no node resolution: they key on
                // (lifetime, service, impl?) string-side, because registration is many-to-many and membership
                // is resolved model-side at evaluation. That is also why a shadowed name needs nothing here —
                // there is no attribution to get wrong, only a name, and both nodes wear it.
                foreach (FragmentServiceRegistration registration in fragment.ServiceRegistrations)
                    MergeRegistration(registration);

                // Catch edges (GRAMMAR §4.8) key on (src, caught), and their unfiltered- and swallowing-site
                // subsets union under that same key and guard — so a file:line any fragment reports unfiltered
                // (or swallowing) keeps that standing in the merged edge.
                foreach (FragmentCatchEdge catchEdge in fragment.CatchEdges)
                    MergeCatchEdge(fragmentSuppliers, catchEdge);

                // Throw edges (GRAMMAR §4.8) key on (src, thrown).
                foreach (FragmentThrowEdge throwEdge in fragment.ThrowEdges)
                    MergeSimpleEdge(
                        fragmentSuppliers, throwEdge.SourceFullName, throwEdge.ThrownFullName, _throwEdgeSites, throwEdge.Sites);

                // Exposure edges (GRAMMAR §4.9) key on (src, exposed).
                foreach (FragmentExposureEdge exposureEdge in fragment.ExposureEdges)
                    MergeSimpleEdge(
                        fragmentSuppliers, exposureEdge.SourceFullName, exposureEdge.ExposedFullName,
                        _exposureEdgeSites, exposureEdge.Sites);
            }

            return Materialize(fragments);
        }

        private void RecordTargetFramework(CodebaseFragment fragment)
        {
            if (fragment.TargetFramework is not { } targetFramework) return;

            if (!_frameworksByProject.TryGetValue(fragment.ProjectName, out SortedSet<string>? frameworks))
                _frameworksByProject[fragment.ProjectName] = frameworks = new SortedSet<string>(StringComparer.Ordinal);

            frameworks.Add(targetFramework);
        }

        private void DeclareMerged(FragmentType declared, CodebaseFragment fragment, int fragmentIndex)
        {
            string fqn = declared.Facts.FullName;
            NodeKey key = NodeKey.Unshadowed(fqn);
            if (!_nodes.ContainsKey(key))
            {
                _nodes[key] = declared.Facts.ToTypeNode(fragment.ProjectName, isExternal: false);
                _hierarchy[fqn] = declared; // the winning (first) declarer supplies the hierarchy
                _hierarchyFragments[fqn] = fragmentIndex;
                _declaringFrameworks[fqn] = fragment.TargetFramework;
            }
            else
            {
                NoteConflationIfCrossProject(fqn, fragment.ProjectName);
                NoteFrameworkCollapseIfSameProject(fqn, fragment.ProjectName, fragment.TargetFramework);
            }

            // Recorded for EVERY declarer, winner or not: a name this fragment compiles is satisfied by
            // this fragment's assembly, so a reference bound from it is an ordinary intra-solution
            // reference however the attribution fell out. An unknown assembly name adds nothing, leaving
            // the set empty and the shadow test with nothing to disagree with.
            if (!_declaringAssemblies.TryGetValue(fqn, out HashSet<string>? assemblies))
                _declaringAssemblies[fqn] = assemblies = new HashSet<string>(StringComparer.Ordinal);
            if (fragment.AssemblyName is { Length: > 0 } assemblyName) assemblies.Add(assemblyName);

            SortedSet<FragmentSite> sites = FragmentSiteSets.For(_declarationSites, fqn);
            foreach (FragmentSite site in declared.DeclarationSites) sites.Add(site);
        }

        // A second (or later) declarer of an already-declared FQN. When its project name differs from the
        // winner's, this is same-FQN cross-project conflation: the facts and ProjectName keep following the
        // first declarer, while selections and edge attribution reach every declarer through the recorded
        // roster — record the loser against the type. A matching project name is a project's own several
        // target frameworks, which the sibling below answers for; the loser set collapses a multi-framework
        // loser to one entry.
        private void NoteConflationIfCrossProject(string fqn, string laterProjectName)
        {
            string winner = _nodes[NodeKey.Unshadowed(fqn)].ProjectName;
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
            if (!string.Equals(_nodes[NodeKey.Unshadowed(fqn)].ProjectName, laterProjectName, StringComparison.Ordinal)) return;
            if (laterFramework is null) return;
            if (!_declaringFrameworks.TryGetValue(fqn, out string? winningFramework) || winningFramework is null) return;
            if (string.Equals(winningFramework, laterFramework, StringComparison.Ordinal)) return;

            // One note per project, so only the first collapse's winner is kept: fragments arrive in
            // (name, framework) order, making it the earliest-extracted framework that actually won a
            // shared type.
            _multiFrameworkWinners.TryAdd(laterProjectName, winningFramework);
        }

        // One note per conflated type, naming every project that declares it. Grouping is what keeps a type
        // stubbed by several sibling projects from spending a line per stub — six types shadowed across six
        // projects cost six lines rather than sixteen, with nothing dropped. The list joiner degrades to
        // "'A' and 'B'" at two items, so a single loser reads as a plain sentence.
        private string ConflationNote(string fqn)
        {
            string winner = _nodes[NodeKey.Unshadowed(fqn)].ProjectName;
            SortedSet<string> losers = _conflatedLosers[fqn];

            string declarers = JoinWithAnd([$"'{winner}'", .. losers.Select(loser => $"'{loser}'")]);
            string selections = JoinWithAnd([.. losers.Select(loser => $"arch.Project('{loser}')")]);

            return $"Type '{fqn}' is declared by projects {declarers}; its facts and "
                   + $"project attribution follow '{winner}' (the first declarer), but {selections} "
                   + "selections include it too, and each declarer's reference to its own compiled-in "
                   + "copy counts against that declarer alone.";
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

        // The shadowed names regrouped under the project that declares each of them, with that project's
        // supplying assemblies unioned. Grouping by PROJECT rather than by type is the FrameworkCollapseNote
        // grain and is chosen for the same reason its comment gives: the answer is the same for all of them,
        // and a solution carrying a dozen netstandard shims under BCL names would otherwise spend a dozen
        // lines drowning the channel it is trying to be noticed in.
        private Dictionary<string, (SortedSet<string> Types, SortedSet<string> Assemblies)> ShadowedNamesByProject()
        {
            Dictionary<string, (SortedSet<string> Types, SortedSet<string> Assemblies)> byProject = new(StringComparer.Ordinal);

            foreach ((string fqn, SortedSet<string> assemblies) in _shadowingAssemblies)
            {
                string declaringProject = _nodes[NodeKey.Unshadowed(fqn)].ProjectName;
                if (!byProject.TryGetValue(declaringProject, out (SortedSet<string> Types, SortedSet<string> Assemblies) entry))
                    byProject[declaringProject] = entry = (new SortedSet<string>(StringComparer.Ordinal), new SortedSet<string>(StringComparer.Ordinal));

                entry.Types.Add(fqn);
                foreach (string assembly in assemblies) entry.Assemblies.Add(assembly);
            }

            return byProject;
        }

        // One note per project whose declarations share a full name with something a referenced assembly
        // supplies. It states the split rather than a suppression, because nothing is suppressed: both types
        // are in the model, and which one a reference reaches is decided by what that reference actually
        // bound. The reader's question is the one the last clause answers — why a project selection over the
        // declaring project does not reach every use of the name.
        private static string ShadowedNamesNote(string projectName, (SortedSet<string> Types, SortedSet<string> Assemblies) shadowed)
        {
            string types = JoinWithAnd([.. shadowed.Types.Select(type => $"'{type}'")]);
            string assemblies = JoinWithAnd([.. shadowed.Assemblies.Select(assembly => $"'{assembly}'")]);
            string noun = shadowed.Assemblies.Count == 1 ? "assembly" : "assemblies";
            string verb = shadowed.Assemblies.Count == 1 ? "supplies" : "supply";

            return $"Project '{projectName}' declares {types}, which referenced {noun} {assemblies} also {verb}; "
                   + "every reference resolves to whichever of the two the referencing compilation bound, so "
                   + $"a selection over project '{projectName}' reaches the declared one alone.";
        }

        // "A", "A and B", "A, B and C" — each caller formats its own items, so this only joins.
        private static string JoinWithAnd(IReadOnlyList<string> items)
        {
            if (items.Count == 1) return items[0];

            return string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
        }

        // Either nothing in the merge declares this FQN — an ordinary external, one node for it, facts from
        // the first fragment that referenced it — or something does, and the question becomes whether the
        // assembly this fragment bound the name FROM is one of the declaring assemblies. Where it is not,
        // the name denotes two different types and the model carries both.
        private void RecordExternal(FragmentExternal external)
        {
            string fqn = external.Facts.FullName;
            NodeKey key = NodeKey.Unshadowed(fqn);
            if (!_nodes.ContainsKey(key))
            {
                _externalFacts.TryAdd(key, external);
                return;
            }

            NoteShadowIfForeign(fqn, external);
        }

        // The shadow test, stated once. Every guard is a fail-open rather than an optimisation:
        //   • the supplying assembly unknown — an error symbol whose ContainingAssembly is null reaches
        //     extraction as "" — so there is nothing to compare;
        //   • no declaring assembly known at all (a hand-built fragment, a cache written before the
        //     fragment carried the fact), so the same;
        //   • the supplier IS one of the declaring assemblies, which is every ordinary reference into a
        //     project of this solution, whichever project won the attribution.
        // Only a KNOWN disagreement mints anything. Doubt never does.
        private void NoteShadowIfForeign(string fqn, FragmentExternal external)
        {
            string supplying = external.AssemblyName;
            if (supplying.Length == 0) return;
            if (!_declaringAssemblies.TryGetValue(fqn, out HashSet<string>? declaring)) return;
            if (declaring.Count == 0 || declaring.Contains(supplying)) return;

            if (!_shadowingAssemblies.TryGetValue(fqn, out SortedSet<string>? shadowing))
                _shadowingAssemblies[fqn] = shadowing = new SortedSet<string>(StringComparer.Ordinal);

            shadowing.Add(supplying);
            _externalFacts.TryAdd(new NodeKey(fqn, supplying), external);
        }

        // FQN → the assembly THIS fragment bound it from, for the shadowed FQNs it referenced but did not
        // declare. The fragment's externals are exactly that answer: extraction mints one per
        // referenced-not-declared FQN carrying the ContainingAssembly of the symbol it bound. A fragment that
        // declared the FQN itself has no entry — which is what leaves a test project's reference to its OWN
        // stand-in alone. A name nothing shadows is left out because nothing would ever ask about it:
        // NodeKeyFor hands such a name straight back without reaching the map, so the map holds the handful
        // of names in dispute rather than one entry per external reference of every fragment.
        //
        // The binder roster is taken in the same walk, on the same test NodeKeyFor makes: an external whose
        // assembly is one recorded as shadowing the name IS this fragment's compilation reaching the
        // assembly's type rather than the declaration, which is the whole of what the roster claims.
        private Dictionary<string, string> SuppliersOf(CodebaseFragment fragment)
        {
            var suppliers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (FragmentExternal external in fragment.Externals)
            {
                string fqn = external.Facts.FullName;
                if (!_shadowingAssemblies.TryGetValue(fqn, out SortedSet<string>? shadowing)) continue;

                suppliers[fqn] = external.AssemblyName;
                if (shadowing.Contains(external.AssemblyName)) RecordBinder(fqn, fragment.ProjectName);
            }

            return suppliers;
        }

        private void RecordBinder(string fqn, string projectName)
        {
            if (!_shadowBinders.TryGetValue(fqn, out SortedSet<string>? binders))
                _shadowBinders[fqn] = binders = new SortedSet<string>(StringComparer.Ordinal);

            binders.Add(projectName);
        }

        // Which node one fragment's mention of an FQN denotes: its own declared-or-first-declarer node,
        // unless this fragment bound the name from an assembly recorded as shadowing it.
        private NodeKey NodeKeyFor(IReadOnlyDictionary<string, string> suppliers, string fqn)
        {
            if (!_shadowingAssemblies.TryGetValue(fqn, out SortedSet<string>? shadowing)) return NodeKey.Unshadowed(fqn);
            if (!suppliers.TryGetValue(fqn, out string? supplier)) return NodeKey.Unshadowed(fqn);

            return shadowing.Contains(supplier) ? new NodeKey(fqn, supplier) : NodeKey.Unshadowed(fqn);
        }

        private void PopulateHierarchy(TypeNode node, FragmentType declared, IReadOnlyDictionary<string, string> suppliers)
        {
            if (declared.BaseTypeFullName is { } baseFqn) node.BaseType = ResolveNode(NodeKeyFor(suppliers, baseFqn));

            node.Interfaces = declared.Interfaces
                .Select(f => (ITypeInfo)ResolveNode(NodeKeyFor(suppliers, f)))
                .ToList();

            node.Attributes = declared.Attributes
                .Select(f => (ITypeInfo)ResolveNode(NodeKeyFor(suppliers, f)))
                .ToList();

            node.AllInterfaces = declared.AllInterfaces.Select(c => ToConstruction(suppliers, c))
                .ToList();
            node.BaseTypeChain = declared.BaseTypeChain.Select(c => ToConstruction(suppliers, c))
                .ToList();
            node.AttributeConstructions = declared.AttributeConstructions.Select(c => ToConstruction(suppliers, c))
                .ToList();
        }

        private TypeConstruction ToConstruction(IReadOnlyDictionary<string, string> suppliers, FragmentConstruction construction)
        {
            return new TypeConstruction(
                ResolveNode(NodeKeyFor(suppliers, construction.DefinitionFullName)), construction.ConstructedName);
        }

        private static void PopulateMembers(TypeNode node, FragmentType declared)
        {
            node.Members = declared.DeclaredMembers
                .Select(member => member.ToMemberNode(node))
                .ToList();
        }

        // The one edge merge, stated once for every axis that keys on an endpoint pair: each endpoint mapped
        // to the node THIS fragment's compilation bound it to, the self-edge guard (extraction already
        // dropped these, so it is defensive), ResolveNode on BOTH endpoints — the rule that keeps an
        // external endpoint one shared node however many fragments referenced it — and the site union.
        // Returns false for the dropped self-edge, so an axis carrying parallel subsets (catch) gates them
        // on the same guard rather than restating it.
        //
        // A source endpoint is always a type this fragment declared, so NodeKeyFor hands it straight back;
        // mapping it anyway is what makes the self-edge guard compare nodes rather than names, which is the
        // honest comparison once one name can denote two.
        private bool MergeSimpleEdge(
            IReadOnlyDictionary<string, string> suppliers,
            string source,
            string target,
            Dictionary<(NodeKey, NodeKey), SortedSet<FragmentSite>> map,
            IReadOnlyList<FragmentSite> sites)
        {
            NodeKey sourceKey = NodeKeyFor(suppliers, source);
            NodeKey targetKey = NodeKeyFor(suppliers, target);
            if (sourceKey == targetKey) return false;

            ResolveNode(sourceKey);
            ResolveNode(targetKey);
            SortedSet<FragmentSite> merged = FragmentSiteSets.For(map, (sourceKey, targetKey));
            foreach (FragmentSite site in sites) merged.Add(site);
            return true;
        }

        private void MergeMemberEdge(IReadOnlyDictionary<string, string> suppliers, FragmentMemberEdge edge)
        {
            NodeKey sourceKey = NodeKeyFor(suppliers, edge.SourceFullName);
            NodeKey containingKey = NodeKeyFor(suppliers, edge.TargetContainingTypeFullName);

            // Same-type guard mirrors the edge self-drop; extraction already dropped these, so it is defensive.
            if (sourceKey == containingKey) return;

            ResolveNode(sourceKey);
            ResolveNode(containingKey);

            // Member facts are functions of the SymbolId, so the first mention wins and every later one agrees.
            // The containing type is stored as the NODE key rather than the name, because that is what
            // BuildMemberEdges resolves with — a member on a shadowed name would otherwise always be hung on
            // the source declaration, whichever assembly the reference actually bound.
            _memberFacts.TryAdd(edge.MemberSymbolId, new MemberEdgeFacts(containingKey, edge.MemberName, edge.MemberKind));

            SortedSet<FragmentSite> sites = FragmentSiteSets.For(_memberEdgeSites, (sourceKey, edge.MemberSymbolId));
            foreach (FragmentSite site in edge.Sites) sites.Add(site);
        }

        private void MergeCatchEdge(IReadOnlyDictionary<string, string> suppliers, FragmentCatchEdge edge)
        {
            if (!MergeSimpleEdge(suppliers, edge.SourceFullName, edge.CaughtFullName, _catchEdgeSites, edge.Sites)) return;

            // Both subsets key on the same NODE pair the edge itself did, not on the names — Materialize
            // looks them up with the keys _catchEdgeSites holds, so a subset keyed on anything else would
            // silently miss on a shadowed name and read as "no unfiltered site here".
            (NodeKey Source, NodeKey Caught) key = (NodeKeyFor(suppliers, edge.SourceFullName), NodeKeyFor(suppliers, edge.CaughtFullName));

            // The unfiltered subset unions under the same key and the same guard. Unioning the UNFILTERED sites
            // is what keeps a `#if`-divergent filter honest: a (file, line) filtered in one fragment and
            // unfiltered in another reads unfiltered here, the truthful answer for a ban.
            SortedSet<FragmentSite> unfilteredSites = FragmentSiteSets.For(_catchEdgeUnfilteredSites, key);
            foreach (FragmentSite site in edge.UnfilteredSites) unfilteredSites.Add(site);

            // The swallowing subset unions the same way, and for the same reason: a (file, line) that rethrows
            // in one fragment and swallows in another reads swallowing here.
            SortedSet<FragmentSite> swallowingSites = FragmentSiteSets.For(_catchEdgeSwallowingSites, key);
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
        ///     The node <paramref name="nodeKey" /> denotes: the declared-anywhere node for a plain
        ///     fully-qualified name, else a shallow external node minted once from the first-fragment-wins
        ///     facts table — which is both an ordinary external and, under a shadow key, the assembly's
        ///     half of a name a project also declares. Every key reachable here was recorded by whichever
        ///     fragment references it, so the table always has an entry when the key names no declaration.
        /// </summary>
        private TypeNode ResolveNode(NodeKey nodeKey)
        {
            if (_nodes.TryGetValue(nodeKey, out TypeNode? node)) return node;

            FragmentExternal external = _externalFacts[nodeKey];
            node = external.Facts.ToTypeNode(external.AssemblyName, isExternal: true);
            _nodes[nodeKey] = node;
            return node;
        }

        private CodebaseModel Materialize(IReadOnlyList<CodebaseFragment> fragments)
        {
            foreach ((string fqn, SortedSet<FragmentSite> sites) in _declarationSites)
            {
                TypeNode node = _nodes[NodeKey.Unshadowed(fqn)];
                node.DeclarationSites = FragmentSiteSets.Locations(sites);
                node.FilePaths = FragmentSiteSets.FilePaths(sites);
            }

            // The conflation kept as a fact rather than only as the sentence ConflationNote composes from the
            // same table. A consumer that has to ACT on it — the survey, which must not render a project's
            // reference to its own compiled-in copy as an edge to the declarer that won — cannot read prose.
            foreach ((string fqn, SortedSet<string> losers) in _conflatedLosers)
                _nodes[NodeKey.Unshadowed(fqn)].AlsoDeclaredBy = losers.ToList();

            // Ordered by name, then by the two facts that tell a shadowed name's two nodes apart — the
            // declaration first, then the supplying assemblies ordinal. One name can denote a source
            // declaration and a referenced assembly's type of that name, so FullName alone is not a total
            // order, and without the tie-break the remainder falls through to dictionary enumeration
            // order, costing every rendered document its byte-stability.
            List<TypeNode> types = _nodes.Values
                .OrderBy(n => n.FullName, StringComparer.Ordinal)
                .ThenBy(n => n.IsExternal ? 1 : 0)
                .ThenBy(n => n.ProjectName, StringComparer.Ordinal)
                .ToList();

            List<ReferenceEdge> edges = FragmentSiteSets.OrderedPairs(
                _edgeSites, NodeKey.Ordinal, NodeKey.Ordinal,
                (src, tgt, sites) => new ReferenceEdge(_nodes[src], _nodes[tgt], FragmentSiteSets.Locations(sites)));

            List<MemberEdge> memberEdges = BuildMemberEdges();

            List<ConstructorEdge> constructorEdges = FragmentSiteSets.OrderedPairs(
                _constructorEdgeSites, NodeKey.Ordinal, NodeKey.Ordinal,
                (src, ctor, sites) => new ConstructorEdge(_nodes[src], _nodes[ctor], FragmentSiteSets.Locations(sites)));

            List<InjectionEdge> injectionEdges = FragmentSiteSets.OrderedPairs(
                _injectionEdgeSites, NodeKey.Ordinal, NodeKey.Ordinal,
                (src, injected, sites) => new InjectionEdge(_nodes[src], _nodes[injected], FragmentSiteSets.Locations(sites)));

            // All three site lists come out of SortedSets, so each is (file, line) ordered and each is a subset
            // of the one before it; an edge with no unfiltered (or no swallowing) site materializes the empty list.
            List<CatchEdge> catchEdges = FragmentSiteSets.OrderedPairs(
                _catchEdgeSites, NodeKey.Ordinal, NodeKey.Ordinal,
                (src, caught, sites) => new CatchEdge(
                    _nodes[src], _nodes[caught], FragmentSiteSets.Locations(sites),
                    _catchEdgeUnfilteredSites.TryGetValue((src, caught), out SortedSet<FragmentSite>? unfiltered) ? FragmentSiteSets.Locations(unfiltered) : [],
                    _catchEdgeSwallowingSites.TryGetValue((src, caught), out SortedSet<FragmentSite>? swallowing) ? FragmentSiteSets.Locations(swallowing) : []));

            List<ThrowEdge> throwEdges = FragmentSiteSets.OrderedPairs(
                _throwEdgeSites, NodeKey.Ordinal, NodeKey.Ordinal,
                (src, thrown, sites) => new ThrowEdge(_nodes[src], _nodes[thrown], FragmentSiteSets.Locations(sites)));

            List<ExposureEdge> exposureEdges = FragmentSiteSets.OrderedPairs(
                _exposureEdgeSites, NodeKey.Ordinal, NodeKey.Ordinal,
                (src, exposed, sites) => new ExposureEdge(_nodes[src], _nodes[exposed], FragmentSiteSets.Locations(sites)));

            List<ServiceRegistration> serviceRegistrations = FragmentSiteSets.OrderedRegistrations(
                _registrationSites,
                (lifetime, service, impl, sites) => new ServiceRegistration(
                    lifetime, service, impl, FragmentSiteSets.Locations(sites)));

            // The advisory notes: the two project-level kinds first, each ordinal by project, then the
            // per-type kind (ordinal by FQN) — coarse fact before fine, and each sorted on the key it
            // groups by, so the list is stable across runs regardless of the order the distinct notes were
            // first raised.
            IEnumerable<string> frameworkCollapseNotes = _multiFrameworkWinners.Keys
                .OrderBy(projectName => projectName, StringComparer.Ordinal)
                .Select(FrameworkCollapseNote);
            IEnumerable<string> shadowedNameNotes = ShadowedNamesByProject()
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => ShadowedNamesNote(entry.Key, entry.Value));
            IEnumerable<string> conflationNotes = _conflatedLosers.Keys
                .OrderBy(fqn => fqn, StringComparer.Ordinal)
                .Select(ConflationNote);
            List<string> mergeNotes = frameworkCollapseNotes
                .Concat(shadowedNameNotes)
                .Concat(conflationNotes)
                .ToList();

            return new CodebaseModel(
                types, edges, memberEdges, constructorEdges, injectionEdges, catchEdges, throwEdges,
                exposureEdges, serviceRegistrations, BuildProjects(fragments), BuildShadowedNames(), mergeNotes);
        }

        // The split kept as a fact rather than only as the sentence ShadowedNamesNote composes from the same
        // tables, for the reason AlsoDeclaredBy exists beside ConflationNote: the consumers that must ACT on
        // it — the survey's coverage statement, a rule author asking whose facts won — cannot read prose.
        //
        // The binder roster is indexed rather than looked up defensively: an entry in _shadowingAssemblies
        // exists only because some fragment's external minted it, and that same fragment's supplier pass
        // records it as a binder, so a shadowed name with no binder is not a state the merge can reach.
        private List<ShadowedName> BuildShadowedNames()
        {
            return _shadowingAssemblies
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new ShadowedName(
                    entry.Key,
                    _nodes[NodeKey.Unshadowed(entry.Key)].ProjectName,
                    entry.Value.ToList(),
                    _shadowBinders[entry.Key].ToList()))
                .ToList();
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
                reference = new MemberReference(_nodes[facts.ContainingNodeKey], facts.Name, symbolId, facts.Kind);
                memberReferences[symbolId] = reference;
                return reference;
            }

            return FragmentSiteSets.OrderedPairs(
                _memberEdgeSites, NodeKey.Ordinal, StringComparer.Ordinal,
                (src, symbolId, sites) => new MemberEdge(_nodes[src], MemberReferenceFor(symbolId), FragmentSiteSets.Locations(sites)));
        }

        // A project node states two facts the merge already computed — every framework its fragments
        // carried, and, where they shared a type, the one whose facts those types took. This is the only
        // ProjectNode construction site in the tree, so the tables reach the model here or nowhere.
        private List<ProjectNode> BuildProjects(IReadOnlyList<CodebaseFragment> fragments)
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
                .Select(kv => new ProjectNode(
                    kv.Key, kv.Value.ToList(), memberByProject[kv.Key], TargetFrameworksOf(kv.Key),
                    _multiFrameworkWinners.GetValueOrDefault(kv.Key)))
                .ToList();
        }

        // Stated only where there is more than one framework to state: a single-framework project — and
        // every hand-built input, whose fragments carry no framework at all — has nothing a name does not
        // already say.
        private IReadOnlyList<string>? TargetFrameworksOf(string projectName)
        {
            SortedSet<string>? frameworks = _frameworksByProject.GetValueOrDefault(projectName);
            return frameworks is { Count: > 1 } ? frameworks.ToList() : null;
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
        ///     The merge-side facts a <see cref="MemberReference" /> needs beyond its SymbolId: the declaring type's
        ///     node key — the key rather than the bare name, because a name a project declares and a referenced
        ///     assembly supplies denotes two nodes and the member hangs on the one its reference bound — plus name
        ///     and kind.
        /// </summary>
        private readonly record struct MemberEdgeFacts(NodeKey ContainingNodeKey, string Name, MemberKind Kind);

        /// <summary>
        ///     Which node a mention of a name denotes: the fully-qualified name, plus the assembly supplying it
        ///     where the name is one a project declares and a referenced assembly shadows.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The merge's two key spaces, told apart by type. A table indexed by <em>name</em> —
        ///         <c>_declarationSites</c>, <c>_conflatedLosers</c>, <c>_hierarchy</c>,
        ///         <c>_shadowingAssemblies</c> — stays keyed by string, so every crossing into the node
        ///         tables spells <see cref="Unshadowed" /> and a crossing left out does not compile.
        ///     </para>
        ///     <para>
        ///         Its equality is ordinal on both fields by construction (a record struct over
        ///         <see langword="string" /> fields compares them with
        ///         <see cref="EqualityComparer{T}.Default" />), so the tables keyed on it take no comparer
        ///         argument at all — one fewer thing that can be spelled differently on one axis.
        ///     </para>
        /// </remarks>
        private readonly record struct NodeKey(string FullName, string? SupplyingAssembly)
        {
            /// <summary>
            ///     The node a name denotes where nothing shadows the binding: the declaration wherever anything
            ///     declares the name, else the one external node minted for it.
            /// </summary>
            public static NodeKey Unshadowed(string fullName)
            {
                return new NodeKey(fullName, null);
            }

            /// <summary>
            ///     The order every table pairing endpoints materializes in: ordinal by name, then a name's
            ///     declaration ahead of its shadows and those ordinal by supplying assembly.
            /// </summary>
            /// <remarks>
            ///     Spelled once here rather than per axis, for the reason
            ///     <see cref="FragmentSiteSets.OrderedPairs{TFirst,TSecond,TValue,TOut}" /> exists at all: a
            ///     sort restated per table is a sort that can come to be spelled two ways, and a rendered
            ///     document's byte-stability is what pays for it.
            /// </remarks>
            public static IComparer<NodeKey> Ordinal { get; } = Comparer<NodeKey>.Create(CompareOrdinal);

            // Null sorts first, which is exactly what puts the declaration ahead of the assembly's half
            // (string.CompareOrdinal answers -1 for a null left operand).
            private static int CompareOrdinal(NodeKey left, NodeKey right)
            {
                int byName = string.CompareOrdinal(left.FullName, right.FullName);
                return byName != 0 ? byName : string.CompareOrdinal(left.SupplyingAssembly, right.SupplyingAssembly);
            }
        }
    }
}
