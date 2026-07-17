namespace Meridian.Operations.Invoicing;

/// <summary>A shipment's assembled invoice: its money lines and their total.</summary>
internal sealed record Invoice(string ShipmentId, IReadOnlyList<InvoiceLine> Lines, decimal Total);