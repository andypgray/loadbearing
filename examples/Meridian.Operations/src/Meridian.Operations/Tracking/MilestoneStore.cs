using Meridian.Operations.Tracking.Contracts;

namespace Meridian.Operations.Tracking;

/// <summary>
///     In-memory milestone store, keyed by shipment and seeded with a few demo shipments so the
///     endpoints return something on a cold start.
/// </summary>
internal sealed class MilestoneStore
{
    private readonly Dictionary<string, List<ShipmentMilestone>> byShipment = new(StringComparer.Ordinal);

    public MilestoneStore()
    {
        // SHIP-1001 runs the full lifecycle; its discharge and gate-out dates drive a real
        // demurrage charge that crosses all three tariff tiers. The other two stop partway, so
        // their invoices show fewer lines.
        Add(new ShipmentMilestone("SHIP-1001", MilestoneKind.Booked, new DateOnly(2026, 2, 20)));
        Add(new ShipmentMilestone("SHIP-1001", MilestoneKind.Departed, new DateOnly(2026, 2, 24)));
        Add(new ShipmentMilestone("SHIP-1001", MilestoneKind.Arrived, new DateOnly(2026, 3, 1)));
        Add(new ShipmentMilestone("SHIP-1001", MilestoneKind.Discharged, new DateOnly(2026, 3, 2)));
        Add(new ShipmentMilestone("SHIP-1001", MilestoneKind.GateOut, new DateOnly(2026, 3, 20)));

        Add(new ShipmentMilestone("SHIP-1002", MilestoneKind.Booked, new DateOnly(2026, 3, 5)));
        Add(new ShipmentMilestone("SHIP-1002", MilestoneKind.Departed, new DateOnly(2026, 3, 9)));

        Add(new ShipmentMilestone("SHIP-1003", MilestoneKind.Booked, new DateOnly(2026, 3, 12)));
    }

    /// <summary>Appends a milestone to the shipment's ordered list.</summary>
    public void Add(ShipmentMilestone milestone)
    {
        if (!byShipment.TryGetValue(milestone.ShipmentId, out var milestones))
        {
            milestones = [];
            byShipment[milestone.ShipmentId] = milestones;
        }

        milestones.Add(milestone);
    }

    /// <summary>Returns the shipment's milestones, or an empty list if none are recorded.</summary>
    public IReadOnlyList<ShipmentMilestone> For(string shipmentId)
    {
        return byShipment.TryGetValue(shipmentId, out var milestones) ? milestones : [];
    }
}