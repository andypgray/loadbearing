namespace Meridian.Operations.Invoicing.Contracts;

/// <summary>Facade for building a shipment's invoice on demand.</summary>
public interface IInvoiceRun
{
    /// <summary>Builds and returns the invoice for the given shipment from its current milestones.</summary>
    InvoiceView GenerateFor(string shipmentId);
}