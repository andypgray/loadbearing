using Meridian.Clearance;
using Microsoft.Data.SqlClient;
using Zphil.LoadBearing;
using Zphil.LoadBearing.Packs.DotNet;

namespace Meridian.ArchSpec;

/// <summary>
///     Meridian's architecture spec — the freight-forwarding monolith mid-migration, carrying all
///     three postures on one codebase: Enforce for the law it already keeps, Migrate for the three
///     ratchets being worked off (inline SQL in controllers; ambient-clock reads; Task-returning
///     methods without the Async suffix), and Quarantine for the ISO 6346 clearance engine, contained
///     behind its gateway. Five rules are Meridian's own; two come from the shared
///     <c>DotNetGuidance</c> pack, taken a la carte at the postures this codebase warrants.
/// </summary>
public sealed class MeridianArchSpec : IArchitectureSpec
{
    /// <inheritdoc />
    public void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "Meridian.Domain.*");
        Layer web = arch.Layer("Web", "Meridian.Web.*");

        arch.Rule("layering/domain-independent")
            .Enforce(domain.MustNotReference(web))
            .Because("Domain holds the booking and rate model the rest of the system depends on; it must not reach up into the web tier.")
            .Fix("Define the abstraction in Meridian.Domain and implement it in Meridian.Web.");

        arch.Rule("naming/controllers")
            .Enforce(arch.Types.InNamespace("Meridian.Web.Controllers.*").MustHaveSuffix("Controller"))
            .Because("Request handlers are found by their `*Controller` name — by routing and by agents reading the code; keep the convention total.");

        arch.Rule("data-access/no-inline-sql")
            .Migrate(
                "Controllers open SqlConnection and run inline SQL directly.",
                arch.Namespace("Meridian.Web.Controllers.*")
                    .MustNotReference(typeof(SqlConnection), typeof(SqlCommand)))
            .Because("Data access behind a repository can be tested and swapped; SQL in the request path cannot.")
            .Fix("Move the SQL into a repository; see BookingRepository.");

        arch.Rule("time/inject-clock")
            .Migrate(
                "Code reads the ambient clock directly.",
                web.Except(arch.Types.WithNameMatching("SystemClock"))
                    .MustNotUse(
                        () => DateTime.Now,
                        () => DateTime.UtcNow))
            .Because("Cutoffs, demurrage, and ETA stamps read from the wall clock cannot be tested at a fixed instant; an injected IClock makes the moment an input.")
            .Fix("Take IClock in the constructor; see BookingsController.");

        // Two rules taken from the shared DotNetGuidance pack rather than written again. The posture is
        // Meridian's to choose, and the two differ: the naming convention is genuinely behind here, so it
        // ratchets against a baseline, while nothing builds a second container, so that one is law from
        // the start. The subject for the naming rule is the two layers rather than a project, so the rule
        // anchors on no single project and stays in the root context rather than churning the per-project
        // cards; it also keeps Program.cs's top-level statements out of the subject.
        DotNetGuidance.AsyncSuffix(arch, arch.AnyOf(domain, web),
            PackPosture.Migrate("Repository and controller methods return Task without the Async suffix."),
            "Rename the method to end in Async and update its callers; see the interface and its implementation together.");

        DotNetGuidance.NoBuildServiceProvider(arch, arch.Types, PackPosture.Enforce);

        arch.Scope("clearance/engine")
            .Quarantine(arch.Namespace("Meridian.Clearance.*"))
            .BoundaryOnlyVia(typeof(IClearanceGateway), typeof(ClearanceGateway))
            .Dragons("ISO 6346 check digit: the letter-value table skips every multiple of 11 " +
                     "(A=10, B=12 … U=32); the gaps are load-bearing — linearizing the table " +
                     "breaks every real container number. Call in only through IClearanceGateway.")
            .Because("The check-digit table implements a published external standard with no cleaner target shape; contain it behind the gateway rather than change it.");
    }
}