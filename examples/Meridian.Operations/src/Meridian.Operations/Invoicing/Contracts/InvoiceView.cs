namespace Meridian.Operations.Invoicing.Contracts;

/// <summary>
///     The invoice returned across the module boundary. Internal line objects are flattened to
///     display strings and the total is carried as a single amount.
/// </summary>
public sealed record InvoiceView(string ShipmentId, IReadOnlyList<string> Lines, decimal Total);