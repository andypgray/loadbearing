using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     The captured baselines for a whole check run, keyed by rule ID. A rule with a section here has
///     a captured baseline, and the violations its entries identify are grandfathered: reported, but
///     not failing the check. A rule with no section has no captured baseline, and every violation of
///     it fails the check. Pass one to <c>ArchChecker.Check</c>, or <see cref="Empty" /> for a run
///     with no baselines at all.
/// </summary>
public sealed class BaselineIndex
{
    private readonly IReadOnlyDictionary<string, RuleBaseline> _sections;

    /// <summary>
    ///     Builds an index over sections already read from baseline files, keyed by rule ID. Give the
    ///     dictionary an ordinal comparer: lookups go through it, so its comparer decides how a rule ID is
    ///     matched. A rule whose baseline file has not been captured is simply absent from the dictionary.
    /// </summary>
    public BaselineIndex(IReadOnlyDictionary<string, RuleBaseline> sections)
    {
        _sections = Guard.NotNull(sections, nameof(sections));
    }

    /// <summary>Gets the index in which no rule has a captured baseline, so every violation fails the check.</summary>
    public static BaselineIndex Empty { get; } = new(new Dictionary<string, RuleBaseline>(StringComparer.Ordinal));

    /// <summary>
    ///     Looks up the captured section for <paramref name="ruleId" />, returning false for a rule with
    ///     no captured baseline. A rule captured while it had no violations is present with zero entries
    ///     and returns true, which is a different answer: that rule grandfathers nothing because there was
    ///     nothing to grandfather, where an absent rule grandfathers nothing because nothing has been
    ///     captured for it yet.
    /// </summary>
    public bool TryGet(string ruleId, out RuleBaseline? baseline)
    {
        return _sections.TryGetValue(Guard.NotNull(ruleId, nameof(ruleId)), out baseline);
    }
}
