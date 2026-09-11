using Meridian.Operations.Demurrage;
using Zphil.LoadBearing;

namespace Meridian.Operations.ArchSpec;

/// <summary>
///     The operations subsystem's architecture spec — Meridian's modular monolith, where the
///     module boundaries are drawn by namespace inside one project rather than by separate
///     assemblies. Each module keeps its internals to itself and is reached through a Contracts
///     surface; the module dependency graph is explicit and acyclic; and the demurrage engine is
///     quarantined behind its calculator facade.
/// </summary>
public sealed class OperationsArchSpec : IArchitectureSpec
{
    /// <inheritdoc />
    public void Define(Arch arch)
    {
        Layer dispatch = arch.Layer("Dispatch", "Meridian.Operations.Dispatch.*")
            .Purpose("Dispatch assigns haulage legs to drivers: the dispatch board and the driver roster behind its " +
                     "Contracts surface.");
        Layer tracking = arch.Layer("Tracking", "Meridian.Operations.Tracking.*")
            .Purpose("Tracking owns the shipment milestone timeline that every other module reads.");
        Layer invoicing = arch.Layer("Invoicing", "Meridian.Operations.Invoicing.*")
            .Purpose("Invoicing assembles a shipment's invoice from its milestones and its demurrage charge.");
        Layer demurrage = arch.Layer("Demurrage", "Meridian.Operations.Demurrage.*")
            .Purpose("Demurrage computes the charge for a container held past its free time, behind its calculator " +
                     "facade.");
        Layer host = arch.Layer("Host", "Meridian.Operations.Host.*")
            .Purpose("Host is the composition root: it wires the modules through their Contracts surfaces and " +
                     "serves the HTTP endpoints.");

        Selection dispatchContracts = arch.Namespace("Meridian.Operations.Dispatch.Contracts.*");
        Selection trackingContracts = arch.Namespace("Meridian.Operations.Tracking.Contracts.*");
        Selection invoicingContracts = arch.Namespace("Meridian.Operations.Invoicing.Contracts.*");

        arch.Rule("modules/internals")
            .Enforce(arch.Each(dispatch, tracking, invoicing)
                         .Except(dispatchContracts, trackingContracts, invoicingContracts)
                         .MustOnlyBeReferencedByItself())
            .Because("Every module is reached only through its Contracts surface, so the dispatch board and roster, the milestone store and log, and the invoice assembler and reconciler all stay swappable; a reference into any of them from another module turns a private implementation detail into a contract its owner can no longer revise without breaking a caller.")
            .Fix("Depend on the module's `Contracts` type — `IDispatchBoard`, `ITrackingLog`, `IInvoiceRun` — instead of reaching into its internals.");

        arch.Rule("modules/dispatch/outbound")
            .Enforce(dispatch.MustOnlyReference(trackingContracts))
            .Because("The module dependency graph is kept explicit and acyclic: dispatch consumes tracking's milestone contracts to gate a haulage leg and reaches nothing else, so the only arrow out of dispatch is the one drawn here and the monolith can still be split along its module lines.");

        arch.Rule("modules/tracking/outbound")
            .Enforce(tracking.MustOnlyReferenceItself())
            .Because("Tracking is the leaf of the module graph: it owns the shipment milestone timeline that every other module reads and depends on no module in turn, so nothing it does can pull another module's state into that shared source of truth.");

        arch.Rule("modules/tracking/event-naming")
            .Enforce(tracking.WithSuffix("Event").Must(t => t.IsRecord, description: "be declared as records"))
            .Because("The `*Event` values are what tracking projects across the module boundary and, later, onto a bus; declaring them as records makes them immutable and compared by value, so an event cannot be mutated after it is published or matched by reference identity.")
            .Fix("Declare the `*Event` type as a `record`.");

        // demurrage is listed here as the whole layer, not just its Contracts/facade surface: the
        // reconciler's grandfathered reach into FreeTimeCalendar is owned by the demurrage/engine
        // quarantine below, which baselines that one legacy edge. An Enforce rule cannot carry a
        // baseline, so naming only the facade here would turn the same edge into an
        // un-grandfatherable red — two rules fighting over one reference.
        arch.Rule("modules/invoicing/outbound")
            .Enforce(invoicing.MustOnlyReference(trackingContracts, demurrage))
            .Because("Invoicing prices a shipment from tracking's milestone contracts and the demurrage charge and integrates with nothing else, so billing's dependencies stay the two it actually needs and the module graph stays legible.");

        arch.Rule("modules/host/outbound")
            .Enforce(host.MustOnlyReference(
                dispatchContracts,
                trackingContracts,
                invoicingContracts,
                arch.Type<IDemurrageCalculator>(),
                arch.Type<DemurrageCalculator>()))
            .Because("The host is the composition root and the only place that sees every module at once; it wires them through their Contracts surfaces and the demurrage calculator facade alone, so no module's internals leak into the wiring and the boundaries the other rules draw are not quietly bypassed here.");

        arch.Scope("demurrage/engine")
            .Quarantine(demurrage)
            .BoundaryOnlyVia(typeof(IDemurrageCalculator), typeof(DemurrageCalculator))
            .Dragons("Demurrage engine: it counts free-time then billable days between discharge and gate-out and prices them across tariff tiers. The free-time clock advances only on port working days, and billing is first-day-exclusive, last-day-inclusive per the carrier tariff sheet; counting calendar days instead, or 'correcting' that off-by-one, reprices every real container. The tariff tiers are non-contiguous and keyed by a day's billable ordinal, not by calendar span. Call in only through IDemurrageCalculator.")
            .Because("The day counting and the tariff table encode a published carrier tariff sheet with no cleaner target shape; the charges come out right precisely because of the conventions that read like bugs, so the engine is contained behind its calculator facade rather than tidied.");
    }
}
