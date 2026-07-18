namespace Zphil.LoadBearing.Model;

/// <summary>
///     The member selection minted by the <c>.Members</c>/<c>.Properties</c>/<c>.Fields</c>/
///     <c>.Events</c> projections (GRAMMAR §4.6) — every projection except <c>.Methods</c>, which mints
///     the specialized <see cref="MethodSelection" />. Carries only its <see cref="MemberKindFilter" />;
///     the shared member adjectives clone it through <see cref="Rebuild" />.
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