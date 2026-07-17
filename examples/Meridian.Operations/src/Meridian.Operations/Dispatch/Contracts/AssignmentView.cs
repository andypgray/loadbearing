namespace Meridian.Operations.Dispatch.Contracts;

/// <summary>An assignment of a driver and truck to a haulage leg, as returned across the module boundary.</summary>
public sealed record AssignmentView(
    string ShipmentId,
    int Sequence,
    string Origin,
    string Destination,
    string Driver,
    string Truck);