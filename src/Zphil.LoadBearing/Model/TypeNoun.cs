using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     A single type — <c>arch.Type(typeof(SqlConnection))</c>. Reference fragment is the simple
///     name, "`SqlConnection`"; the full <see cref="System.Type" /> is retained so colliding simple
///     names can be qualified in a target list (GRAMMAR §5.1, §6).
/// </summary>
internal sealed class TypeNoun(Type type) : SelectionNoun
{
    /// <summary>The reflected type; retains the FQN for collision qualification.</summary>
    internal Type Type { get; } = type;

    internal override string Locative => string.Empty;

    internal override string ReferenceFragment => ProseFormat.Backtick(TypeName.Simple(Type));
}