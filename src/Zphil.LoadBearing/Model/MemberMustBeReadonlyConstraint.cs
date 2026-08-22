using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Fields.MustBeReadonly()</c> → "must be readonly" (GRAMMAR §5.7). Fields-only by receiver type.
/// </summary>
/// <remarks>
///     A <c>const</c> field <b>satisfies</b> the verb. Const is readonly's superset — readonly, static and
///     compile-time at once — so redding one would demand something weaker than what is already there.
///     That lives in the checker rather than in each spec, because <c>.Fields.ThatAreStatic()</c> sweeps
///     every constant a type declares and no author should have to remember it.
///     The name is spelled after the C# keyword it renders, not after
///     <see cref="IMemberInfo.IsReadOnly" />, the fact it reads: a verb mirrors its fragment.
/// </remarks>
internal sealed class MemberMustBeReadonlyConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be readonly";
}
