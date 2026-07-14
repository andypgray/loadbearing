using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Model;

/// <summary>Posture stage for a rule anchor: sets the posture on the registered node (GRAMMAR §3.2).</summary>
internal sealed class RuleBuilder(RuleRegistration registration) : IRuleBuilder
{
    public IEnforceRule Enforce(Constraint constraint)
    {
        registration.Posture = Posture.Enforce;
        registration.Constraint = Guard.NotNull(constraint, nameof(constraint));
        return new EnforceRuleBuilder(registration);
    }

    public IMigrateRule Migrate(string from, Constraint to)
    {
        registration.Posture = Posture.Migrate;
        registration.MigrateFrom = from;
        registration.Constraint = Guard.NotNull(to, nameof(to));
        return new MigrateRuleBuilder(registration);
    }
}