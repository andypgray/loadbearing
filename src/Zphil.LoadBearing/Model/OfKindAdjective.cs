using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.OfKind(TypeKind.Interface)</c> substitutes the subject head plural: "interfaces"
///     (GRAMMAR §5.2). It never renders inline — it replaces the default "types" head.
/// </summary>
internal sealed class OfKindAdjective(TypeKind kind) : SelectionAdjective
{
    internal TypeKind Kind { get; } = kind;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Head;

    internal override string Fragment => ProseFormat.KindPlural(Kind);
}