namespace Meridian.Operations.Tracking.Contracts;

/// <summary>Domain event: a shipment's container has been discharged at the destination port.</summary>
public sealed record ShipmentDischargedEvent(string ShipmentId, DateOnly At);