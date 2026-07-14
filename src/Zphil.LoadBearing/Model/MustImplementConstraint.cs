using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustImplement(typeof(IDisposable))</c> → "must implement `IDisposable`" (GRAMMAR §5.3).</summary>
internal sealed class MustImplementConstraint(Selection subject, Type type) : Constraint(subject)
{
    /// <summary>The interface the subject must implement.</summary>
    internal Type Type { get; } = type;

    internal override string VerbPhrase => "must implement " + ProseFormat.Backtick(TypeName.Simple(Type));
}