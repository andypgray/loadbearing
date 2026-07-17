namespace Meridian.Operations.Tracking.Contracts;

/// <summary>A single milestone reached by a shipment, on a given date.</summary>
public sealed record ShipmentMilestone(string ShipmentId, MilestoneKind Kind, DateOnly At);