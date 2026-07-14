namespace Zphil.LoadBearing.Model;

/// <summary>Trailer/option stage for a <c>Migrate</c> rule. Each appends so repeats are detectable (§8 item 6).</summary>
internal sealed class MigrateRuleBuilder(RuleRegistration registration) : IMigrateRule
{
    public IMigrateRule Because(string because)
    {
        registration.Becauses.Add(because);
        return this;
    }

    public IMigrateRule Fix(string fix)
    {
        registration.Fixes.Add(fix);
        return this;
    }

    public IMigrateRule Baseline(string path)
    {
        registration.Baselines.Add(path);
        return this;
    }

    public IMigrateRule WhileYoureThere(MigrationPolicy policy)
    {
        registration.Policies.Add(policy);
        return this;
    }
}