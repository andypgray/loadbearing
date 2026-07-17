namespace Meridian.Operations.Tracking.Contracts;

/// <summary>Domain event: a shipment has departed its origin port.</summary>
public sealed record ShipmentDepartedEvent(string ShipmentId, DateOnly At);