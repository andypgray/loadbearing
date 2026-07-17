namespace Meridian.Operations.Tracking.Contracts;

/// <summary>Facade for recording and reading a shipment's tracking milestones.</summary>
public interface ITrackingLog
{
    /// <summary>Records a milestone for a shipment.</summary>
    void Record(ShipmentMilestone milestone);

    /// <summary>Returns the milestones recorded for a shipment, in the order they were recorded.</summary>
    IReadOnlyList<ShipmentMilestone> MilestonesFor(string shipmentId);
}