using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The verb available on a field selection alone — the <c>Fields</c> projection of a
///     <see cref="Selection" />. <c>MustBeReadonly</c> asks a question only a field can answer, so it
///     does not compile on a method, property, event or plain member selection.
/// </summary>
// Receiver-typed to FieldSelection exactly like .Returning is to MethodSelection, so the verb is
// uncompilable off .Methods, .Properties, .Events and .Members — no validation rule to write and no
// runtime refusal to render (GRAMMAR §5.7).
public static class FieldSelectionConstraints
{
    /// <summary>
    ///     States that every selected field must be declared <c>readonly</c>, such as
    ///     <c>arch.Types.Fields.ThatAreStatic().MustBeReadonly()</c>, and returns the
    ///     <see cref="Constraint" /> to hand to <c>Enforce</c> or <c>Migrate</c>. A <c>const</c> field
    ///     passes as well: it is already more constrained than a <c>readonly</c> one, so failing it would
    ///     be asking for something weaker than what is already there. Every other field fails the check.
    /// </summary>
    public static Constraint MustBeReadonly(this FieldSelection subject)
    {
        return new MemberMustBeReadonlyConstraint(Guard.NotNull(subject, nameof(subject)));
    }
}
