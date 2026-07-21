namespace Zphil.LoadBearing.MyAppCleanSpec;

/// <summary>
///     A spec for the MyApp fixture whose rules all hold, so <c>check</c> exits 0: Billing must not
///     reference Web (it doesn't), interfaces under <c>MyApp.*</c> are I-prefixed (they are —
///     <c>IHandler</c>, <c>IBillingFacade</c>), and the Migrate rule's two <c>DataTable</c> sites
///     (Home + Invoice) are <em>fully grandfathered</em> by <c>arch/clean-baseline.json</c> — so a
///     ratcheted rule with all debt captured still passes.
/// </summary>
public sealed class MyAppCleanSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("layering/billing-independent")
            .Enforce(arch.Namespace("MyApp.Legacy.Billing.*").MustNotReference(arch.Namespace("MyApp.Web.*")))
            .Because("Billing must not reach up into the web layer.");

        arch.Rule("naming/interfaces")
            .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*").MustHavePrefix("I"))
            .Because("House naming convention; agents grep by I-prefix.");

        arch.Rule("data-access/no-inline-sql")
            .Migrate(
                "Controllers build DataTables inline (legacy Active Record style).",
                arch.Namespace("MyApp.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
            .Baseline("arch/clean-baseline.json")
            .Because("Repository pattern for testability.");
    }
}