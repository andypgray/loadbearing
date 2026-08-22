using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     The member selection minted by the <c>.Fields</c> projection (GRAMMAR §4.6) — a
///     <see cref="MemberSelection" /> specialized to fields, which is what makes <c>MustBeReadonly</c>
///     available.
/// </summary>
/// <remarks>
///     The readonly verb is fields-only, so it binds by receiver type here and is uncompilable on the
///     other projections by construction (GRAMMAR §3.2), exactly as <c>.Returning</c> is on
///     <see cref="MethodSelection" />. The shared member adjectives preserve this type (they are generic
///     self-type extensions), so <c>.Fields.ThatAreStatic().WithPrefix("s_")</c> type-checks in any order
///     and the verb stays reachable after either.
/// </remarks>
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
