namespace Zphil.LoadBearing.Internal;

/// <summary>
///     The one <c>(first, params more)</c> operand-list builder behind the fluent surface. Every verb
///     overload that takes at least one operand differs only in what it projects each operand into, and
///     the shape that makes a zero-operand call uncompilable is the same in all of them.
/// </summary>
/// <remarks>
///     The parameters are named <c>first</c> and <c>more</c> deliberately: the guards spell
///     <c>nameof(first)</c> and <c>nameof(more)</c> here, so every
///     <see cref="ArgumentException.ParamName" /> a caller sees is the name on the public overload
///     they actually called.
/// </remarks>
internal static class OperandList
{
    /// <summary>
    ///     The guarded, projected operand list: <paramref name="first" /> then each of
    ///     <paramref name="more" />, null-checked by parameter name and passed through
    ///     <paramref name="project" />.
    /// </summary>
    internal static IReadOnlyList<TOut> OneOrMore<TIn, TOut>(TIn first, TIn[] more, Func<TIn, TOut> project)
        where TIn : class
    {
        var list = new List<TOut>(1 + more.Length) { project(Guard.NotNull(first, nameof(first))) };
        foreach (TIn item in more) list.Add(project(Guard.NotNull(item, nameof(more))));

        return list;
    }
}
