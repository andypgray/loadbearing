namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A type as one particular construction of it: the open or non-generic <see cref="Definition" />
///     together with the name it was written as. For a non-generic type the two names are the same; for a
///     closed generic they differ, <c>MyApp.Web.IHandler&lt;MyApp.Web.InvoiceCreated&gt;</c> being a
///     construction of <c>MyApp.Web.IHandler&lt;T&gt;</c>. It appears in the hierarchy facts a type carries:
///     <see cref="TypeNode.AllInterfaces" />, <see cref="TypeNode.BaseTypeChain" /> and
///     <see cref="TypeNode.AttributeConstructions" />.
/// </summary>
/// <remarks>
///     Keeping both names is what lets a rule tell "any construction" from "that one":
///     <c>Implementing(typeof(IHandler&lt;&gt;))</c> matches on <see cref="Definition" />'s
///     <see cref="TypeNode.FullName" />, and <c>Implementing(typeof(IHandler&lt;Order&gt;))</c> on this
///     <see cref="FullName" />. Reference edges keep no constructions at all: they record definitions.
/// </remarks>
public sealed class TypeConstruction
{
    internal TypeConstruction(TypeNode definition, string fullName)
    {
        Definition = definition;
        FullName = fullName;
    }

    /// <summary>
    ///     Gets the open-generic or non-generic type this constructs. Its <see cref="TypeNode.FullName" />
    ///     carries the declared type-parameter names, as in <c>MyApp.Web.IHandler&lt;T&gt;</c>.
    /// </summary>
    public TypeNode Definition { get; }

    /// <summary>
    ///     Gets the constructed name: fully qualified, with type arguments substituted. Equal to
    ///     <see cref="Definition" />'s <see cref="TypeNode.FullName" /> for a non-generic type.
    /// </summary>
    public string FullName { get; }
}
