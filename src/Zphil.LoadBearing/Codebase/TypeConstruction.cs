namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A specific construction of a type as it appears in a hierarchy fact: the open or
///     non-generic <see cref="Definition" /> node plus the <em>constructed</em> display
///     <see cref="FullName" />. For a non-generic type the two names coincide; for a closed generic
///     they diverge — <c>MyApp.Web.IHandler&lt;MyApp.Web.InvoiceCreated&gt;</c> constructs
///     <c>MyApp.Web.IHandler&lt;T&gt;</c>.
/// </summary>
/// <remarks>
///     Reference edges stay definition-level (v1 scope); this construction-preserving data exists so
///     the checker can honour GRAMMAR §5.2 — <c>Implementing(typeof(IHandler&lt;Order&gt;))</c> means
///     "that construction exactly", while <c>typeof(IHandler&lt;&gt;)</c> means "any construction".
///     The open case matches on <see cref="Definition" />'s <see cref="TypeNode.FullName" />; the
///     closed case matches on this <see cref="FullName" />.
/// </remarks>
public sealed class TypeConstruction
{
    internal TypeConstruction(TypeNode definition, string fullName)
    {
        Definition = definition;
        FullName = fullName;
    }

    /// <summary>The open-generic or non-generic definition node (its FullName carries declared parameter names).</summary>
    public TypeNode Definition { get; }

    /// <summary>
    ///     The constructed display name (substituted type arguments, fully qualified). Equals
    ///     <see cref="Definition" />'s <see cref="TypeNode.FullName" /> for a non-generic type.
    /// </summary>
    public string FullName { get; }
}