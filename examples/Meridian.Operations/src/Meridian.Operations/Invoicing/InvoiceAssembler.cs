using Meridian.Operations.Tracking.Contracts;

namespace Meridian.Operations.Invoicing;

/// <summary>Turns a shipment's milestones and its demurrage charge into invoice money lines.</summary>
internal sealed class InvoiceAssembler
{
    private const decimal OceanTransportCharge = 1850m;

    /// <summary>Builds the invoice's money lines and total from the shipment's milestones and demurrage charge.</summary>
    public Invoice Assemble(
        string shipmentId,
        IReadOnlyList<ShipmentMilestone> milestones,
        string containerId,
        decimal demurrageCharge)
    {
        List<InvoiceLine> lines = [];

        bool departed = milestones.Any(milestone => milestone.Kind == MilestoneKind.Departed);
        if (departed) lines.Add(new InvoiceLine("Ocean transport", OceanTransportCharge));

        if (demurrageCharge > 0m) lines.Add(new InvoiceLine($"Demurrage (container {containerId})", demurrageCharge));

        decimal total = lines.Sum(line => line.Amount);
        return new Invoice(shipmentId, lines, total);
    }
}