namespace Zphil.LoadBearing.Model;

/// <summary>All solution-declared types — <c>arch.Types</c>. Fragment: "types" (GRAMMAR §5.1).</summary>
internal sealed class TypesNoun : SelectionNoun
{
    private TypesNoun()
    {
    }

    /// <summary>The single shared instance; <c>Types</c> carries no per-instance state.</summary>
    internal static TypesNoun Instance { get; } = new();

    internal override string Locative => string.Empty;
}