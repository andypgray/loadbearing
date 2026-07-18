namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     One type declared by a single compilation, captured as pure data: its
///     <see cref="TypeFacts">scalar facts</see>, its declaration sites, and its hierarchy expressed
///     entirely <em>by FQN</em> so the merge can rewire it to the shared node instances. Its owning
///     <see cref="CodebaseFragment.ProjectName" /> is the node's <c>ProjectName</c> — a declared node's
///     project is the declaring fragment's, so it is not repeated here.
/// </summary>
/// <remarks>
///     Ordering mirrors the builder exactly and must be preserved through any serialization round-trip:
///     <see cref="Interfaces" /> and <see cref="Attributes" /> are in <b>symbol order, unsorted</b>;
///     <see cref="AllInterfaces" /> and <see cref="AttributeConstructions" /> are sorted ordinal by
///     constructed name; <see cref="BaseTypeChain" /> is nearest-first (its order is meaningful and never
///     sorted); <see cref="DeclarationSites" /> is in <see cref="FragmentSite" /> order;
///     <see cref="DeclaredMembers" /> is sorted ordinal by member <see cref="MemberFacts.SymbolId" />.
///     <see cref="BaseTypeFullName" /> is null for interfaces and <see cref="object" />.
/// </remarks>
internal sealed record FragmentType(
    TypeFacts Facts,
    IReadOnlyList<FragmentSite> DeclarationSites,
    string? BaseTypeFullName,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> Attributes,
    IReadOnlyList<FragmentConstruction> AllInterfaces,
    IReadOnlyList<FragmentConstruction> BaseTypeChain,
    IReadOnlyList<FragmentConstruction> AttributeConstructions,
    IReadOnlyList<FragmentMember> DeclaredMembers);