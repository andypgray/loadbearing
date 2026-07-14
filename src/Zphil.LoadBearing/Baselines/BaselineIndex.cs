using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     The parsed baseline sections for a whole check run, keyed by rule ID. A present key is a
///     captured section (which the checker partitions violations against); an absent key is an
///     uncaptured rule (every violation red plus a bootstrap hint). <see cref="Empty" /> is the
///     no-baselines index the two-argument checker overload and the <c>baseline --init</c> current-state
///     pass use.
/// </summary>
public sealed class BaselineIndex
{
    private readonly IReadOnlyDictionary<string, RuleBaseline> _sections;

    /// <summary>Builds an index over already-parsed rule sections.</summary>
    public BaselineIndex(IReadOnlyDictionary<string, RuleBaseline> sections)
    {
        _sections = Guard.NotNull(sections, nameof(sections));
    }

    /// <summary>The empty index — no rule is captured.</summary>
    public static BaselineIndex Empty { get; } = new(new Dictionary<string, RuleBaseline>(StringComparer.Ordinal));

    /// <summary>
    ///     Gets the captured section for <paramref name="ruleId" />. Returns false for an uncaptured
    ///     rule (absent key), which is deliberately distinct from a captured-empty section (present key,
    ///     zero entries).
    /// </summary>
    public bool TryGet(string ruleId, out RuleBaseline? baseline)
    {
        return _sections.TryGetValue(Guard.NotNull(ruleId, nameof(ruleId)), out baseline);
    }
}