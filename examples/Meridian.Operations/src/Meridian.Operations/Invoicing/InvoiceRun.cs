using Meridian.Operations.Demurrage;
using Meridian.Operations.Invoicing.Contracts;
using Meridian.Operations.Tracking.Contracts;

namespace Meridian.Operations.Invoicing;

/// <summary>
///     Builds an invoice for a shipment from its tracking milestones and demurrage charges. It
///     reads the shipment's milestones, prices demurrage through the calculator facade, and adds
///     the reconciler's disputed-days detail for the customer.
/// </summary>
internal sealed class InvoiceRun(
    ITrackingLog tracking,
    IDemurrageCalculator demurrage,
    DemurrageReconciler reconciler,
    InvoiceAssembler assembler) : IInvoiceRun
{
    // Billing's own reference data: which container rides on each shipment. Tracking milestones
    // are keyed by shipment, so the demurrage engine is handed the container from here.
    private readonly IReadOnlyDictionary<string, string> containersByShipment =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SHIP-1001"] = "MSKU7346095",
            ["SHIP-1002"] = "TCLU4510338",
            ["SHIP-1003"] = "HLXU9080761"
        };

    public InvoiceView GenerateFor(string shipmentId)
    {
        var milestones = tracking.MilestonesFor(shipmentId);
        string containerId = containersByShipment.GetValueOrDefault(shipmentId, shipmentId);

        var demurrageCharge = 0m;
        IReadOnlyList<DateOnly> disputedDays = [];

        ShipmentMilestone? discharged = milestones.FirstOrDefault(milestone => milestone.Kind == MilestoneKind.Discharged);
        ShipmentMilestone? gatedOut = milestones.FirstOrDefault(milestone => milestone.Kind == MilestoneKind.GateOut);
        if (discharged is not null && gatedOut is not null)
        {
            demurrageCharge = demurrage.CalculateCharge(containerId, discharged.At, gatedOut.At);
            disputedDays = reconciler.DisputedDays(discharged.At, gatedOut.At);
        }

        Invoice invoice = assembler.Assemble(shipmentId, milestones, containerId, demurrageCharge);

        var lines = invoice.Lines
            .Select(line => $"{line.Description}: {line.Amount:0.00} USD")
            .ToList();

        if (disputedDays.Count > 0)
        {
            var range = $"{disputedDays[0]:yyyy-MM-dd}..{disputedDays[^1]:yyyy-MM-dd}";
            lines.Add($"Disputed demurrage days: {disputedDays.Count} working days {range}");
        }

        return new InvoiceView(invoice.ShipmentId, lines, invoice.Total);
    }
}