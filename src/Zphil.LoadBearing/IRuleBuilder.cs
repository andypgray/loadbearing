using Zphil.LoadBearing.Fluent;

namespace Zphil.LoadBearing;

/// <summary>
///     The step after <c>arch.Rule(id)</c>: a rule takes exactly one posture, so the only members here
///     are <see cref="Enforce" /> and <see cref="Migrate" />. A rule left without either is reported
///     when the spec is loaded.
/// </summary>
public interface IRuleBuilder
{
    /// <summary>
    ///     Makes the constraint binding: every violation fails the check. Follow with <c>Because</c>,
    ///     which is required; <c>Fix</c> and <c>Citation</c> are optional.
    /// </summary>
    IEnforceRule Enforce(Constraint constraint);

    /// <summary>
    ///     Declares a migration in progress: <paramref name="from" /> describes, in one line of prose, the
    ///     pattern the code still follows, and <paramref name="to" /> is the constraint it is moving
    ///     toward. Violations recorded in the rule's baseline file are reported as grandfathered and do not
    ///     fail the check; any other violation fails it, so the debt can only shrink. Follow with
    ///     <c>Because</c>, which is required; <c>Baseline</c>, <c>WhileYoureThere</c>, <c>Fix</c> and
    ///     <c>Citation</c> are optional.
    /// </summary>
    IMigrateRule Migrate(string from, Constraint to);
}
