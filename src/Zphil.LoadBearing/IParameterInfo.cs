namespace Zphil.LoadBearing;

/// <summary>
///     One declared parameter of a method, as a member predicate sees it: the ordered entries of
///     <see cref="IMemberInfo.Parameters" />.
/// </summary>
public interface IParameterInfo
{
    /// <summary>The parameter's declared name.</summary>
    string Name { get; }

    /// <summary>
    ///     The parameter's type as a full name — namespace and containing types included, and a generic
    ///     spelled with its declared type-parameter names (<c>System.IProgress&lt;T&gt;</c>), so every
    ///     construction of one generic type reads the same. Four edges are worth knowing:
    ///     <c>ref</c>, <c>in</c> and <c>out</c> do not change it; an array parameter reports the array type
    ///     (<c>System.Threading.CancellationToken[]</c>); a nullable value type reports
    ///     <c>System.Nullable&lt;T&gt;</c>; and a parameter typed by the method's own type parameter
    ///     reports that parameter's name (<c>T</c>).
    /// </summary>
    string TypeFullName { get; }
}
