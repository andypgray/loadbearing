using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     One grandfathered violation's identity in a baseline (GRAMMAR §4.3). A dependency-verb entry
///     carries <see cref="Source" /> and <see cref="Target" /> symbol IDs; a shape/naming/inheritance/
///     attribute/escape-hatch entry carries only <see cref="Subject" />. The unused slots are null.
///     IDs are Roslyn <c>DocumentationCommentId</c> strings (<c>T:</c> forms in v1), so an entry is
///     stable across file moves and formatting. Value equality is ordinal over all three slots.
///     An optional <see cref="Because" /> attribution rides along but is excluded from equality —
///     identity is the ID slots only, so ratchet set operations never fork on annotation.
/// </summary>
public sealed class BaselineEntry : IEquatable<BaselineEntry>
{
    private BaselineEntry(string? source, string? target, string? subject, string? because)
    {
        Source = source;
        Target = target;
        Subject = subject;
        Because = because;
    }

    /// <summary>The referencing type's symbol ID (edge entry), or null for a subject entry.</summary>
    public string? Source { get; }

    /// <summary>The referenced type's symbol ID (edge entry), or null for a subject entry.</summary>
    public string? Target { get; }

    /// <summary>The offending type's symbol ID (subject entry), or null for an edge entry.</summary>
    public string? Subject { get; }

    /// <summary>Why this entry is grandfathered (single line, non-blank), or null when unattributed. Excluded from equality.</summary>
    public string? Because { get; }

    /// <inheritdoc />
    public bool Equals(BaselineEntry? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(Source, other.Source, StringComparison.Ordinal)
               && string.Equals(Target, other.Target, StringComparison.Ordinal)
               && string.Equals(Subject, other.Subject, StringComparison.Ordinal);
    }

    /// <summary>An edge entry keyed by (source, target) symbol IDs — the dependency verbs.</summary>
    public static BaselineEntry ForEdge(string source, string target)
    {
        return new BaselineEntry(Guard.NotNull(source, nameof(source)), Guard.NotNull(target, nameof(target)), null, null);
    }

    /// <summary>A subject entry keyed by one symbol ID — shape/naming/inheritance/attribute/escape verbs.</summary>
    public static BaselineEntry ForSubject(string subject)
    {
        return new BaselineEntry(null, null, Guard.NotNull(subject, nameof(subject)), null);
    }

    /// <summary>A copy of this entry carrying <paramref name="because" /> — same identity, new attribution.</summary>
    public BaselineEntry WithBecause(string because)
    {
        bool blankOrMultiline = string.IsNullOrWhiteSpace(because)
                                || because.IndexOf('\r') >= 0
                                || because.IndexOf('\n') >= 0;
        if (blankOrMultiline)
            throw new ArgumentException("A baseline attribution must be a non-blank single line.", nameof(because));

        return new BaselineEntry(Source, Target, Subject, because);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as BaselineEntry);
    }

    /// <summary>Hand-rolled ordinal hash — <c>System.HashCode</c> is unavailable on netstandard2.0.</summary>
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (Source is null ? 0 : StringComparer.Ordinal.GetHashCode(Source));
            hash = hash * 31 + (Target is null ? 0 : StringComparer.Ordinal.GetHashCode(Target));
            hash = hash * 31 + (Subject is null ? 0 : StringComparer.Ordinal.GetHashCode(Subject));
            return hash;
        }
    }
}