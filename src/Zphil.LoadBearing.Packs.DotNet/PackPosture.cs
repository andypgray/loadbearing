namespace Zphil.LoadBearing.Packs.DotNet;

/// <summary>
///     The posture a spec author chooses for a rule taken from <see cref="DotNetGuidance" />:
///     <see cref="Enforce" />, or <see cref="Migrate" /> with a line saying what the code does today.
///     Every pack method takes one. There is no <c>Quarantine</c> or <c>Caution</c> here, because a
///     pack hands out rules rather than regions.
/// </summary>
public sealed class PackPosture
{
    private PackPosture(Posture posture, string? from)
    {
        Posture = posture;
        From = from;
    }

    /// <summary>Gets the binding posture: the rule must hold, and every violation fails the check.</summary>
    public static PackPosture Enforce { get; } = new(Posture.Enforce, null);

    /// <summary>
    ///     Gets the chosen posture as a <see cref="Zphil.LoadBearing.Posture" /> value:
    ///     <c>Posture.Enforce</c> or <c>Posture.Migrate</c>.
    /// </summary>
    public Posture Posture { get; }

    /// <summary>
    ///     Gets the line describing what the code does today, as given to <see cref="Migrate" />. Null at
    ///     <see cref="Enforce" />.
    /// </summary>
    public string? From { get; }

    /// <summary>
    ///     Chooses the migrating posture: the rule does not hold today, and <paramref name="from" />
    ///     describes in one line of prose the pattern the code still follows. Violations recorded in the
    ///     rule's baseline file are reported as grandfathered and do not fail the check; any other
    ///     violation fails it, so the debt can only shrink. The baseline path stays conventional —
    ///     <c>arch/baselines/{rule-id}.json</c> — so a pack never names one. A blank or multi-line value
    ///     is reported when the spec is loaded, alongside every other spec error.
    /// </summary>
    // from is deliberately unguarded here. Blank or multi-line prose flows into the spec's own
    // validation pass and is reported as BlankProse/MultiLineProse at the pack's file:line, alongside
    // every other spec error — a throw from this factory would report one error and lose the rest.
    public static PackPosture Migrate(string from)
    {
        return new PackPosture(Posture.Migrate, from);
    }
}
