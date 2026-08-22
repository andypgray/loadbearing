using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Properties.MustBeGetOnly()</c> → "must be get-only" (GRAMMAR §5.7). Properties-only by
///     receiver type, like <c>MustAcceptParameter</c> on methods.
/// </summary>
/// <remarks>
///     <b>Strict.</b> A property satisfies the verb only when it declares no setter accessor at all, so
///     <c>{ get; init; }</c> reds: get-only is a claim about the declaration, not about when the write is
///     allowed to happen. The narrower "set banned, init fine" reading stays expressible through the
///     member escape hatch, because <see cref="IMemberInfo.HasInitOnlySetter" /> records the setter's kind
///     separately.
/// </remarks>
internal sealed class MemberMustBeGetOnlyConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be get-only";
}
