using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Implementing(typeof(IHandler&lt;&gt;))</c> → " implementing `IHandler&lt;T&gt;`"
///     (GRAMMAR §5.2). An open generic definition means any construction; rendering uses declared
///     type-parameter names.
/// </summary>
internal sealed class ImplementingAdjective(Type type) : SelectionAdjective
{
    internal Type Type { get; } = type;

    /// <summary>True when the interface was given as an open generic (any construction matches).</summary>
    internal bool IsOpenGeneric { get; } = type.IsGenericTypeDefinition;

    internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

    internal override string Fragment => $" implementing {ProseFormat.Backtick(TypeName.Simple(Type))}";
}