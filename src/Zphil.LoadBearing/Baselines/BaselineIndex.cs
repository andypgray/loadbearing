using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Baselines;

/// <summary>
///     The captured baselines for a whole check run, keyed by rule ID. A rule with a section here has
///     a captured baseline, and the violations its entries identify are grandfathered: reported, but
///     not failing the check. A rule with no section has no captured baseline, and every violation of
///     it fails the check. Reading the baseline files a spec names is the runner's job: only the
///     <c>loadbearing</c> tool and the <c>Zphil.LoadBearing.Xunit</c> adapter build a populated index,
///     so a direct call to <c>ArchChecker.Check</c> passes <see cref="Empty" /> and grandfathers
///     nothing.
/// </summary>
public sealed class BaselineIndex
{
    private readonly Dictionary<string, RuleBaseline> _sections;

    /// <summary>
    ///     Builds an index over sections already read from baseline files, keyed by rule ID. The sections
    ///     are copied into an ordinal dictionary, so a rule ID is matched ordinally whatever comparer the
    ///     caller's own dictionary carried. A rule whose baseline file has not been captured is simply
    ///     absent from the dictionary.
    /// </summary>
    internal BaselineIndex(IReadOnlyDictionary<string, RuleBaseline> sections)
    {
        IReadOnlyDictionary<string, RuleBaseline> given = Guard.NotNull(sections, nameof(sections));

        _sections = given.ToDictionary(section => section.Key, section => section.Value, StringComparer.Ordinal);
    }

    /// <summary>Gets the index in which no rule has a captured baseline, so every violation fails the check.</summary>
    public static BaselineIndex Empty { get; } = new(new Dictionary<string, RuleBaseline>());

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
