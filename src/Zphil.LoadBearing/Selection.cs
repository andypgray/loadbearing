using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     A set of types the spec talks about: the subject of a rule, or the target of a dependency
///     verb. Start one from the methods on <see cref="Arch" /> (<c>Types</c>, <c>Layer</c>,
///     <c>Namespace</c>, <c>Project</c>, <c>Type</c>, <c>AnyOf</c>, <c>Each</c>, <c>Registered</c>);
///     narrow it with adjectives such as <c>InNamespace</c>, <c>Implementing</c>, <c>Named</c> or
///     <c>Except</c>, each of which returns a new selection; finish it with a <c>Must</c> verb, which
///     yields the <see cref="Constraint" /> a rule takes; or project it to its members with
///     <see cref="Members" />, <see cref="Methods" />, <see cref="Properties" />,
///     <see cref="Fields" /> or <see cref="Events" />. Immutable and reusable: assign one to a
///     variable and use it in as many rules as you like, on the <see cref="Arch" /> it was built from.
/// </summary>
// A closed hierarchy: the constructor is private protected, so no foreign assembly can add a node, and
// every node is walkable and renderable by construction. The vocabulary ships as extension methods
// (SelectionAdjectives, SelectionConstraints) that build the internal nodes; the member projections are
// instance properties so that a Layer inherits them (GRAMMAR §4.6). A selection is one SelectionNoun
// plus an ordered list of SelectionAdjective refinements, which is exactly what sentence assembly reads
// (GRAMMAR §6).
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

    /// <summary>
    ///     Gets every member the selected types declare, as a selection to narrow with <c>WithSuffix</c>,
    ///     <c>AttributedWith</c>, <c>ThatAreStatic</c> or <c>Where</c> and finish with a member verb such
    ///     as <c>MustBePublic</c> or <c>MustHaveSuffix</c>.
    /// </summary>
    public MemberSelection Members => new KindMemberSelection(this, MemberKindFilter.Any, Array.Empty<MemberAdjective>());

    /// <summary>
    ///     Gets the methods the selected types declare, as a selection to narrow with <c>Returning</c>
    ///     and the member adjectives and finish with a member verb; <c>MustAcceptParameter</c> is
    ///     available on methods alone.
    /// </summary>
    public MethodSelection Methods => new(this, Array.Empty<MemberAdjective>());

    /// <summary>
    ///     Gets the properties the selected types declare, as a selection to narrow with the member
    ///     adjectives and finish with a member verb; <c>MustBeGetOnly</c> is available on properties
    ///     alone.
    /// </summary>
    public PropertySelection Properties => new(this, Array.Empty<MemberAdjective>());

    /// <summary>
    ///     Gets the fields the selected types declare, as a selection to narrow with the member
    ///     adjectives and finish with a member verb; <c>MustBeReadonly</c> is available on fields alone.
    /// </summary>
    public FieldSelection Fields => new(this, Array.Empty<MemberAdjective>());

    /// <summary>
    ///     Gets the events the selected types declare, as a selection to narrow with the member
    ///     adjectives and finish with a member verb.
    /// </summary>
    public MemberSelection Events => new KindMemberSelection(this, MemberKindFilter.Event, Array.Empty<MemberAdjective>());
}
