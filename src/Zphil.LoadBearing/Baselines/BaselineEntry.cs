using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     One grandfathered violation's identity in a baseline (GRAMMAR §4.3). A dependency-verb entry
///     carries <see cref="Source" /> and <see cref="Target" /> symbol IDs; a shape/naming/inheritance/
///     attribute/escape-hatch entry carries only <see cref="Subject" />. The unused slots are null.
/// </summary>
/// <remarks>
///     <para>
///         IDs are Roslyn <c>DocumentationCommentId</c> strings — <c>T:</c> forms for type subjects and
///         edges, and <c>M:</c>/<c>P:</c>/<c>F:</c>/<c>E:</c> forms for member subjects (§4.6) — so an
///         entry's identity is stable across file moves and formatting. Value equality is ordinal over
///         all three ID slots.
///     </para>
///     <para>
///         Two slots ride beside that identity and are excluded from equality, so ratchet set
///         operations never fork on either: <see cref="Because" />, an <em>annotation</em> nothing
///         compares, and <see cref="SiteCount" />, the <em>measure</em> the ratchet compares an
///         observed site count against. The measure differs from the annotation in two ways that must
///         both hold. It is folded into the file digest, so a hand-edited count is tamper rather than a
///         silently widened allowance. And it is line-grained where identity is format-immune: a
///         reformat that splits or joins two same-target mentions on one line moves the count while
///         leaving the entry it sits on exactly where it was (GRAMMAR §4.3).
///     </para>
/// </remarks>
public sealed class BaselineEntry : IEquatable<BaselineEntry>
{
    private BaselineEntry(string? source, string? target, string? subject, string? because, int? siteCount)
    {
        Source = source;
        Target = target;
        Subject = subject;
        Because = because;
        SiteCount = siteCount;
    }

    /// <summary>The referencing type's symbol ID (edge entry), or null for a subject entry.</summary>
    public string? Source { get; }

    /// <summary>The referenced type's symbol ID (edge entry), or null for a subject entry.</summary>
    public string? Target { get; }

    /// <summary>The offending type's or member's symbol ID (subject entry), or null for an edge entry.</summary>
    public string? Subject { get; }

    /// <summary>Why this entry is grandfathered (single line, non-blank), or null when unattributed. Excluded from equality.</summary>
    public string? Because { get; }

    /// <summary>
    ///     How many distinct <c>file:line</c> sites this entry grandfathers (at least 1), or null when it
    ///     is <em>uncounted</em> — captured before the measure existed, or by a write with no count to
    ///     record. An uncounted entry grandfathers its pair at any size, which is what keeps a partially
    ///     upgraded file valid. Edge entries only; excluded from equality, folded into the digest.
    /// </summary>
    public int? SiteCount { get; }

    /// <summary>
    ///     Whether this is an edge entry — the shape that carries a <see cref="SiteCount" />. A subject
    ///     entry's sites are declarations, so it never carries one (GRAMMAR §4.3).
    /// </summary>
    public bool IsEdge => Source is not null;

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
        return new BaselineEntry(
            Guard.NotNull(source, nameof(source)), Guard.NotNull(target, nameof(target)), null, null, null);
    }

    /// <summary>A subject entry keyed by one symbol ID — shape/naming/inheritance/attribute/escape verbs.</summary>
    public static BaselineEntry ForSubject(string subject)
    {
        return new BaselineEntry(null, null, Guard.NotNull(subject, nameof(subject)), null, null);
    }

    /// <summary>
    ///     <paramref name="entries" /> in the canonical baseline order —
    ///     <c>
    ///         ((Source ?? Subject),
    ///         (Target ?? ""))
    ///     </c>
    ///     , ordinal. Single-sourced here because the on-disk file, a parsed section's
    ///     <see cref="RuleBaseline.Entries" /> and the integrity digest are all computed over it: a drift
    ///     between any two of them would change a stored file's digest without changing an entry.
    /// </summary>
    internal static IReadOnlyList<BaselineEntry> InCanonicalOrder(IEnumerable<BaselineEntry> entries)
    {
        return entries
            .OrderBy(e => e.Source ?? e.Subject, StringComparer.Ordinal)
            .ThenBy(e => e.Target ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A copy of this entry carrying <paramref name="because" /> — same identity, new attribution.</summary>
    /// <remarks>Preserves <see cref="SiteCount" />: attributing an entry never disturbs its measure.</remarks>
    /// <exception cref="ArgumentException"><paramref name="because" /> is blank or spans more than one line.</exception>
    public BaselineEntry WithBecause(string because)
    {
        bool blankOrMultiline = string.IsNullOrWhiteSpace(because)
                                || because.IndexOf('\r') >= 0
                                || because.IndexOf('\n') >= 0;
        if (blankOrMultiline)
            throw new ArgumentException("A baseline attribution must be a non-blank single line.", nameof(because));

        return new BaselineEntry(Source, Target, Subject, because, SiteCount);
    }

    /// <summary>
    ///     A copy of this entry grandfathering <paramref name="siteCount" /> sites — same identity, new
    ///     measure.
    /// </summary>
    /// <remarks>
    ///     Preserves <see cref="Because" />, so the two copy verbs compose in either order and a
    ///     re-recorded count never drops the attribution that justified the entry. Zero is refused rather
    ///     than stored: an entry that grandfathers nothing is a stale entry, which the ratchet reports for
    ///     acceptance instead of recording.
    /// </remarks>
    /// <param name="siteCount">The number of distinct <c>file:line</c> sites; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="siteCount" /> is less than 1.</exception>
    /// <exception cref="InvalidOperationException">This is a subject entry, which carries no measure.</exception>
    public BaselineEntry WithSiteCount(int siteCount)
    {
        if (siteCount < 1)
            throw new ArgumentOutOfRangeException(
                nameof(siteCount), siteCount, "A baseline site count must be at least 1.");

        if (!IsEdge)
            throw new InvalidOperationException(
                "Only an edge entry carries a site count — a subject entry's sites are declarations.");

        return new BaselineEntry(Source, Target, Subject, Because, siteCount);
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
