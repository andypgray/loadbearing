namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     Moves a fragment's stored sites onto the lines a re-shaped document put them on, so an edit that
///     moved lines without changing a fact costs a remap rather than a re-walk of the whole project.
/// </summary>
internal static class FragmentSiteRemapper
{
    /// <summary>
    ///     <paramref name="fragment" /> with every site in a mapped file moved to its new line;
    ///     <see langword="null" /> where some site sits on a line its file's map has no entry for, which
    ///     is the caller's signal to re-walk; and <paramref name="fragment" /> itself where nothing moved.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <paramref name="mapsByFile" /> is keyed by the path a site spells and is read exactly as
    ///         handed over — the caller owns the key comparer, being the side that knows which paths it
    ///         built maps from, and must key it by the repo's own path rule.
    ///     </para>
    ///     <para>
    ///         Each rebuilt site list is re-canonicalized through a <see cref="SortedSet{T}" />, so a join
    ///         that lands two sites on one line collapses them to the one site a cold walk reports. Every
    ///         fragment of a project goes through this per edit, so anything that did not move is returned
    ///         by reference rather than copied, and a fragment that moved nothing allocates nothing.
    ///     </para>
    /// </remarks>
    internal static CodebaseFragment? TryRemap(CodebaseFragment fragment, IReadOnlyDictionary<string, LineMap> mapsByFile)
    {
        var pass = new SitePass(mapsByFile);

        IReadOnlyList<FragmentType> declaredTypes = pass.RemapEach(fragment.DeclaredTypes, pass.RemapType);
        IReadOnlyList<FragmentEdge> edges = pass.RemapEach(
            fragment.Edges, static edge => edge.Sites, static (edge, sites) => edge with { Sites = sites });
        IReadOnlyList<FragmentMemberEdge> memberEdges = pass.RemapEach(
            fragment.MemberEdges, static edge => edge.Sites, static (edge, sites) => edge with { Sites = sites });
        IReadOnlyList<FragmentConstructorEdge> constructorEdges = pass.RemapEach(
            fragment.ConstructorEdges, static edge => edge.Sites, static (edge, sites) => edge with { Sites = sites });
        IReadOnlyList<FragmentInjectionEdge> injectionEdges = pass.RemapEach(
            fragment.InjectionEdges, static edge => edge.Sites, static (edge, sites) => edge with { Sites = sites });
        IReadOnlyList<FragmentCatchEdge> catchEdges = pass.RemapEach(fragment.CatchEdges, pass.RemapCatchEdge);
        IReadOnlyList<FragmentThrowEdge> throwEdges = pass.RemapEach(
            fragment.ThrowEdges, static edge => edge.Sites, static (edge, sites) => edge with { Sites = sites });
        IReadOnlyList<FragmentExposureEdge> exposureEdges = pass.RemapEach(
            fragment.ExposureEdges, static edge => edge.Sites, static (edge, sites) => edge with { Sites = sites });
        IReadOnlyList<FragmentServiceRegistration> registrations = pass.RemapEach(
            fragment.ServiceRegistrations,
            static registration => registration.Sites,
            static (registration, sites) => registration with { Sites = sites });

        // The artifact sites name props and project files, so in practice no C# document's map ever
        // claims one; they go through the same site function anyway rather than being trusted to.
        FragmentSite? targetFrameworksSite = pass.RemapOptional(fragment.TargetFrameworksSite);
        IReadOnlyList<FragmentPackageReference>? packageReferences = pass.RemapPackages(fragment.PackageReferences);
        FragmentSite? isPackableSite = pass.RemapOptional(fragment.IsPackableSite);
        FragmentSite? locksPackagesSite = pass.RemapOptional(fragment.LocksPackagesSite);

        if (pass.Unmappable) return null;
        if (!pass.Moved) return fragment;

        return fragment with
        {
            DeclaredTypes = declaredTypes,
            Edges = edges,
            MemberEdges = memberEdges,
            ConstructorEdges = constructorEdges,
            InjectionEdges = injectionEdges,
            CatchEdges = catchEdges,
            ThrowEdges = throwEdges,
            ExposureEdges = exposureEdges,
            ServiceRegistrations = registrations,
            TargetFrameworksSite = targetFrameworksSite,
            PackageReferences = packageReferences,
            IsPackableSite = isPackableSite,
            LocksPackagesSite = locksPackagesSite
        };
    }

    /// <summary>One <see cref="TryRemap" /> call's walk over one fragment, and the two verdicts it accumulates.</summary>
    /// <remarks>
    ///     <see cref="Moved" /> is what says a rebuilt fragment is worth returning, and is set by
    ///     <see cref="Remap" /> alone so no axis can rebuild a list without the fragment counting as moved.
    ///     <see cref="Unmappable" /> is read only after every axis has run: it is a verdict about the whole
    ///     fragment, and short-circuiting on it would trade a bounded walk for a branch per site.
    /// </remarks>
    private sealed class SitePass
    {
        private readonly IReadOnlyDictionary<string, LineMap> _mapsByFile;

        internal SitePass(IReadOnlyDictionary<string, LineMap> mapsByFile)
        {
            _mapsByFile = mapsByFile;
        }

        /// <summary>Whether any site changed line.</summary>
        internal bool Moved { get; private set; }

        /// <summary>Whether any site sat on a line its file's map has no entry for.</summary>
        internal bool Unmappable { get; private set; }

        /// <summary>
        ///     The axes whose only site-bearing member is one list of sites: read it with
        ///     <paramref name="sitesOf" />, put the remapped one back with <paramref name="withSites" />.
        /// </summary>
        internal IReadOnlyList<T> RemapEach<T>(
            IReadOnlyList<T> items,
            Func<T, IReadOnlyList<FragmentSite>> sitesOf,
            Func<T, IReadOnlyList<FragmentSite>, T> withSites)
            where T : class
        {
            List<T>? rebuilt = null;
            for (var index = 0; index < items.Count; index++)
            {
                T item = items[index];
                IReadOnlyList<FragmentSite> sites = sitesOf(item);
                IReadOnlyList<FragmentSite> remapped = RemapSites(sites);
                bool unchanged = ReferenceEquals(remapped, sites);
                if (unchanged && rebuilt is null) continue;

                rebuilt ??= Carry(items, index);
                rebuilt.Add(unchanged ? item : withSites(item, remapped));
            }

            return rebuilt ?? items;
        }

        /// <summary>The axes carrying more than one site-bearing member, remapped whole by <paramref name="remap" />.</summary>
        internal IReadOnlyList<T> RemapEach<T>(IReadOnlyList<T> items, Func<T, T> remap) where T : class
        {
            List<T>? rebuilt = null;
            for (var index = 0; index < items.Count; index++)
            {
                T item = items[index];
                T remapped = remap(item);
                if (ReferenceEquals(remapped, item) && rebuilt is null) continue;

                rebuilt ??= Carry(items, index);
                rebuilt.Add(remapped);
            }

            return rebuilt ?? items;
        }

        /// <summary>A declared type: its own declaration sites, and each declared member's.</summary>
        internal FragmentType RemapType(FragmentType type)
        {
            IReadOnlyList<FragmentSite> sites = RemapSites(type.DeclarationSites);
            IReadOnlyList<FragmentMember> members = RemapEach(type.DeclaredMembers, RemapMember);
            if (ReferenceEquals(sites, type.DeclarationSites) && ReferenceEquals(members, type.DeclaredMembers)) return type;

            return type with { DeclarationSites = sites, DeclaredMembers = members };
        }

        /// <summary>A catch edge: the clause sites and the two nested subsets of them.</summary>
        internal FragmentCatchEdge RemapCatchEdge(FragmentCatchEdge edge)
        {
            IReadOnlyList<FragmentSite> sites = RemapSites(edge.Sites);
            IReadOnlyList<FragmentSite> unfiltered = RemapSites(edge.UnfilteredSites);
            IReadOnlyList<FragmentSite> swallowing = RemapSites(edge.SwallowingSites);
            if (ReferenceEquals(sites, edge.Sites)
                && ReferenceEquals(unfiltered, edge.UnfilteredSites)
                && ReferenceEquals(swallowing, edge.SwallowingSites))
                return edge;

            return edge with { Sites = sites, UnfilteredSites = unfiltered, SwallowingSites = swallowing };
        }

        /// <summary>One of the fragment's optional artifact sites; <see langword="null" /> stays null.</summary>
        internal FragmentSite? RemapOptional(FragmentSite? site)
        {
            return site is { } known ? Remap(known) : null;
        }

        /// <summary>
        ///     The declared packages, each keeping its name and its position — the list is ordered by name,
        ///     not by site, so it is mapped in place rather than re-canonicalized.
        /// </summary>
        internal IReadOnlyList<FragmentPackageReference>? RemapPackages(IReadOnlyList<FragmentPackageReference>? packages)
        {
            if (packages is null) return null;

            List<FragmentPackageReference>? rebuilt = null;
            for (var index = 0; index < packages.Count; index++)
            {
                FragmentPackageReference package = packages[index];
                FragmentSite remapped = Remap(package.Site);
                if (remapped.Line == package.Site.Line && rebuilt is null) continue;

                rebuilt ??= Carry(packages, index);
                rebuilt.Add(package with { Site = remapped });
            }

            return rebuilt ?? packages;
        }

        private FragmentMember RemapMember(FragmentMember member)
        {
            IReadOnlyList<FragmentSite> sites = RemapSites(member.DeclarationSites);
            return ReferenceEquals(sites, member.DeclarationSites) ? member : member with { DeclarationSites = sites };
        }

        // The same list back where no site in it moved, so an untouched axis costs one pass and no
        // allocation; otherwise a SortedSet, which restores the pinned (file, line) order and collapses
        // the duplicates a join makes.
        private IReadOnlyList<FragmentSite> RemapSites(IReadOnlyList<FragmentSite> sites)
        {
            SortedSet<FragmentSite>? rebuilt = null;
            for (var index = 0; index < sites.Count; index++)
            {
                FragmentSite site = sites[index];
                FragmentSite remapped = Remap(site);
                if (remapped.Line == site.Line && rebuilt is null) continue;

                rebuilt ??= CarrySites(sites, index);
                rebuilt.Add(remapped);
            }

            return rebuilt is null ? sites : rebuilt.ToList();
        }

        private FragmentSite Remap(FragmentSite site)
        {
            if (!_mapsByFile.TryGetValue(site.File, out LineMap? map)) return site;

            if (!map.TryMap(site.Line, out int newLine))
            {
                Unmappable = true;
                return site;
            }

            if (newLine == site.Line) return site;

            Moved = true;
            return new FragmentSite(site.File, newLine);
        }

        // The items already passed over unchanged, carried into the collection the rebuild continues into.
        private static List<T> Carry<T>(IReadOnlyList<T> items, int index)
        {
            var carried = new List<T>(items.Count);
            for (var position = 0; position < index; position++) carried.Add(items[position]);

            return carried;
        }

        private static SortedSet<FragmentSite> CarrySites(IReadOnlyList<FragmentSite> sites, int index)
        {
            var carried = new SortedSet<FragmentSite>();
            for (var position = 0; position < index; position++) carried.Add(sites[position]);

            return carried;
        }
    }
}
