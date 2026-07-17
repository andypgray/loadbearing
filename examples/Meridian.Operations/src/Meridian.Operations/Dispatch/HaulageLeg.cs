namespace Meridian.Operations.Dispatch;

/// <summary>One road leg of a shipment's haulage, from an origin to a destination.</summary>
internal sealed record HaulageLeg(string ShipmentId, int Sequence, string Origin, string Destination);