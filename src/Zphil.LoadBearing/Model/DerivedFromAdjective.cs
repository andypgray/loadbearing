using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.DerivedFrom(typeof(ControllerBase))</c> → " derived from `ControllerBase`" (GRAMMAR §5.2).</summary>
internal sealed class DerivedFromAdjective(Type type) : SelectionAdjective
{
    internal Type Type { get; } = type;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" derived from {ProseFormat.Backtick(TypeName.Simple(Type))}";
}