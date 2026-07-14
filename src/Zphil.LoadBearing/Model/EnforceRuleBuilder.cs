namespace Zphil.LoadBearing.Model;

/// <summary>Trailer stage for an <c>Enforce</c> rule. Each trailer appends so repeats are detectable (§8 item 6).</summary>
internal sealed class EnforceRuleBuilder(RuleRegistration registration) : IEnforceRule
{
    public IEnforceRule Because(string because)
    {
        registration.Becauses.Add(because);
        return this;
    }

    public IEnforceRule Fix(string fix)
    {
        registration.Fixes.Add(fix);
        return this;
    }
}