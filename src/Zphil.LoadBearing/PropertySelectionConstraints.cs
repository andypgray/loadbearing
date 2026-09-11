using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The verb available on a property selection alone — the <c>Properties</c> projection of a
///     <see cref="Selection" />. <c>MustBeGetOnly</c> asks a question only a property can answer, so
///     it does not compile on a method, field, event or plain member selection.
/// </summary>
// Receiver-typed to PropertySelection exactly like .Returning is to MethodSelection, so the verb is
// uncompilable off .Methods, .Fields, .Events and .Members — no validation rule to write and no
// runtime refusal to render (GRAMMAR §5.7).
public static class PropertySelectionConstraints
{
    /// <summary>
    ///     States that every selected property must declare no setter at all, such as
    ///     <c>arch.Types.Properties.MustBeGetOnly()</c>, and returns the <see cref="Constraint" /> to hand
    ///     to <c>Enforce</c> or <c>Migrate</c>. This is a claim about the declaration, so it is strict in
    ///     both directions an author tends to expect otherwise: an <c>init</c>-only setter is still a
    ///     setter and <c>{ get; init; }</c> fails the check, and accessibility makes no difference, so
    ///     <c>private set</c> fails too. A get-only auto-property, an expression-bodied property and a
    ///     property with a getter accessor alone all pass.
    /// </summary>
    public static Constraint MustBeGetOnly(this PropertySelection subject)
    {
        return new MemberMustBeGetOnlyConstraint(Guard.NotNull(subject, nameof(subject)));
    }
}
