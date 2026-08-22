using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     The member selection minted by the <c>.Members</c> and <c>.Events</c> projections (GRAMMAR §4.6) —
///     every projection except <c>.Methods</c>, <c>.Properties</c> and <c>.Fields</c>, which mint the
///     specialized <see cref="MethodSelection" />, <see cref="PropertySelection" /> and
///     <see cref="FieldSelection" /> their kind-only verbs bind to. Carries only its
///     <see cref="MemberKindFilter" />; the shared member adjectives clone it through
///     <see cref="Rebuild" />.
/// </summary>
internal sealed class KindMemberSelection : MemberSelection
{
    internal KindMemberSelection(Selection source, MemberKindFilter kind, IReadOnlyList<MemberAdjective> adjectives)
        : base(source, kind, adjectives)
    {
    }

    private protected override MemberSelection Rebuild(IReadOnlyList<MemberAdjective> adjectives)
    {
        return new KindMemberSelection(Source, Kind, adjectives);
    }
}
