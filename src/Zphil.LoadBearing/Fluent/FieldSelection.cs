using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     The fields the selected types declare, reached with <c>.Fields</c> on a
///     <see cref="Selection" />. Narrow it with the member adjectives (<c>WithSuffix</c>,
///     <c>WithPrefix</c>, <c>WithNameMatching</c>, <c>AttributedWith</c>, <c>ThatAreStatic</c>,
///     <c>Where</c>), each of which hands back a field selection again, so the field-only call stays
///     reachable whatever the order; finish it with a member verb such as <c>MustHavePrefix</c> or
///     <c>MustBePrivate</c>, or with <c>MustBeReadonly</c>, which fields alone accept. A <c>const</c>
///     field satisfies <c>MustBeReadonly</c>. An enum's values are not fields here: an enum type
///     declares no members a selection can reach. Immutable and reusable: every call hands back a new
///     selection and leaves this one as it was.
/// </summary>
public sealed class FieldSelection : MemberSelection
{
    internal FieldSelection(Selection source, IReadOnlyList<MemberAdjective> adjectives)
        : base(source, MemberKindFilter.Field, adjectives)
    {
    }

    private protected override MemberSelection Rebuild(IReadOnlyList<MemberAdjective> adjectives)
    {
        return new FieldSelection(Source, adjectives);
    }
}
