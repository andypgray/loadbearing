namespace Zphil.LoadBearing.Packs.DotNet;

/// <summary>
///     The posture a consumer chooses for a pack rule, plus the one thing a posture needs beyond the
///     <see cref="Posture" /> enum: <c>Migrate</c>'s counter-prior prose, which is project matter the
///     pack cannot know. Pairing the two in a closed type makes "Enforce with prose" and "Migrate
///     without prose" uncompilable, rather than a throw out of <c>Define</c>.
/// </summary>
/// <remarks>
///     There is deliberately no <c>Quarantine</c> or <c>Caution</c> factory: a scope is a region, not a
///     rule a pack hands out.
/// </remarks>
public sealed class PackPosture
{
    private PackPosture(Posture posture, string? from)
    {
        Posture = posture;
        From = from;
    }

    /// <summary>The law: the rule must hold, and a violation is red.</summary>
    public static PackPosture Enforce { get; } = new(Posture.Enforce, null);

    /// <summary>
    ///     The chosen posture — the public <see cref="Zphil.LoadBearing.Posture" /> enum, so the pack mints no rival
    ///     vocabulary.
    /// </summary>
    public Posture Posture { get; }

    /// <summary>The <c>Migrate</c> counter-prior prose, or null at <see cref="Enforce" />.</summary>
    public string? From { get; }

    /// <summary>
    ///     Ratcheted tech debt: the rule is red today, and <paramref name="from" /> describes the
    ///     current state in the consuming project's own words.
    /// </summary>
    /// <remarks>
    ///     <paramref name="from" /> is deliberately unguarded here. Blank or multi-line prose flows
    ///     into the spec's own validation pass and is reported as <c>BlankProse</c>/<c>MultiLineProse</c>
    ///     at the pack's <c>file:line</c>, alongside every other spec error — a throw from this factory
    ///     would report one error and lose the rest.
    /// </remarks>
    public static PackPosture Migrate(string from)
    {
        return new PackPosture(Posture.Migrate, from);
    }
}
