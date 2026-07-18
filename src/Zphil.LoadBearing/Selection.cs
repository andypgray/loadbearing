using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     An immutable, reusable set of types the spec talks about (GRAMMAR §3). A closed class
///     hierarchy: the constructor is <c>private protected</c>, so foreign assemblies cannot
///     introduce selection nodes, and every node in the model is walkable and renderable by
///     construction. The growing vocabulary ships as public extension methods
///     (<see cref="SelectionAdjectives" />) that build the internal nodes — the one exception being
///     the member-subject projections (<see cref="Members" />/<see cref="Methods" />/…), which are
///     instance properties so a <see cref="Layer" /> inherits them (GRAMMAR §4.6).
/// </summary>
/// <remarks>
///     A selection is a composite: a single <see cref="SelectionNoun" /> plus an ordered list of
///     <see cref="SelectionAdjective" /> refinements (GRAMMAR §6 subject assembly reads exactly
///     these two). Modal-verb extensions turn a selection into a terminal <see cref="Constraint" />;
///     the projection properties turn it into a <see cref="MemberSelection" /> over the members of
///     the selected types (GRAMMAR §4.6).
/// </remarks>
public abstract class Selection
{
    private protected Selection(Arch owner)
    {
        Owner = owner;
    }

    /// <summary>The <see cref="Arch" /> this selection was minted on (GRAMMAR §3.2 fresh-instance contract).</summary>
    internal Arch Owner { get; }

    /// <summary>The noun head of the selection.</summary>
    internal abstract SelectionNoun Noun { get; }

    /// <summary>The ordered adjective refinements applied to the noun.</summary>
    internal abstract IReadOnlyList<SelectionAdjective> Adjectives { get; }

    /// <summary>All declared members of the selected types (GRAMMAR §4.6): "members of {ref}".</summary>
    public MemberSelection Members => new KindMemberSelection(this, MemberKindFilter.Any, Array.Empty<MemberAdjective>());

    /// <summary>
    ///     The declared methods of the selected types (GRAMMAR §4.6): "methods of {ref}". A
    ///     <see cref="MethodSelection" />, so <c>.Returning(...)</c> is available.
    /// </summary>
    public MethodSelection Methods => new(this, Array.Empty<MemberAdjective>());

    /// <summary>The declared properties of the selected types (GRAMMAR §4.6): "properties of {ref}".</summary>
    public MemberSelection Properties => new KindMemberSelection(this, MemberKindFilter.Property, Array.Empty<MemberAdjective>());

    /// <summary>The declared fields of the selected types (GRAMMAR §4.6): "fields of {ref}".</summary>
    public MemberSelection Fields => new KindMemberSelection(this, MemberKindFilter.Field, Array.Empty<MemberAdjective>());

    /// <summary>The declared events of the selected types (GRAMMAR §4.6): "events of {ref}".</summary>
    public MemberSelection Events => new KindMemberSelection(this, MemberKindFilter.Event, Array.Empty<MemberAdjective>());
}