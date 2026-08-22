using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     The member selection minted by the <c>.Properties</c> projection (GRAMMAR §4.6) — a
///     <see cref="MemberSelection" /> specialized to properties, which is what makes
///     <c>MustBeGetOnly</c> available.
/// </summary>
/// <remarks>
///     The get-only verb is properties-only, so it binds by receiver type here and is uncompilable on the
///     other projections by construction (GRAMMAR §3.2), exactly as <c>.Returning</c> is on
///     <see cref="MethodSelection" />. The shared member adjectives preserve this type (they are generic
///     self-type extensions), so <c>.Properties.WithSuffix("Id").ThatAreStatic()</c> type-checks in any
///     order and the verb stays reachable after either.
/// </remarks>
public sealed class PropertySelection : MemberSelection
{
    internal PropertySelection(Selection source, IReadOnlyList<MemberAdjective> adjectives)
        : base(source, MemberKindFilter.Property, adjectives)
    {
    }

    private protected override MemberSelection Rebuild(IReadOnlyList<MemberAdjective> adjectives)
    {
        return new PropertySelection(Source, adjectives);
    }
}
