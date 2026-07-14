using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustDeriveFrom(typeof(ControllerBase))</c> → "must derive from `ControllerBase`" (GRAMMAR §5.3).</summary>
internal sealed class MustDeriveFromConstraint(Selection subject, Type type) : Constraint(subject)
{
    /// <summary>The base type the subject must derive from.</summary>
    internal Type Type { get; } = type;

    internal override string VerbPhrase => "must derive from " + ProseFormat.Backtick(TypeName.Simple(Type));
}