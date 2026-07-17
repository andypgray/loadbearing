namespace Meridian.Operations.Dispatch;

/// <summary>A driver and truck assigned to a haulage leg.</summary>
internal sealed record Assignment(HaulageLeg Leg, string Driver, string Truck);