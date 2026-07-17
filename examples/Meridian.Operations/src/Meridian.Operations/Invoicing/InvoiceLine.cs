namespace Meridian.Operations.Invoicing;

/// <summary>One line of an invoice: a description and the amount it carries.</summary>
internal sealed record InvoiceLine(string Description, decimal Amount);