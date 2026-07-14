namespace Zphil.LoadBearing;

/// <summary>
///     The stage after <c>arch.Rule(id)</c>: a rule cannot exist without a posture, so the only
///     methods here are the posture verbs (GRAMMAR §3.2). A dangling <c>arch.Rule("x")</c> with no
///     posture is caught by validation (§8 item 2), not silently dropped.
/// </summary>
public interface IRuleBuilder
{
    /// <summary>The law: the constraint must hold; violation is red.</summary>
    IEnforceRule Enforce(Constraint constraint);

    /// <summary>Ratcheted tech debt: a descriptive current state and a prescriptive target.</summary>
    IMigrateRule Migrate(string from, Constraint to);
}