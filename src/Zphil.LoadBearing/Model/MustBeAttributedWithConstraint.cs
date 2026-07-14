using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustBeAttributedWith(typeof(ApiControllerAttribute))</c> → "must be attributed with
///     `[ApiController]`" (GRAMMAR §5.3) — <c>Attribute</c> suffix stripped, name bracketed.
/// </summary>
internal sealed class MustBeAttributedWithConstraint(Selection subject, Type type) : Constraint(subject)
{
    /// <summary>The attribute type the subject must carry.</summary>
    internal Type Type { get; } = type;

    internal override string VerbPhrase => "must be attributed with " + ProseFormat.Backtick(ProseFormat.AttributeName(Type));
}