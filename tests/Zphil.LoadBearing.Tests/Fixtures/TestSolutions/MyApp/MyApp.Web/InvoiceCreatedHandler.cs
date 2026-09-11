namespace MyApp.Web;

public class InvoiceCreatedHandler : IHandler<InvoiceCreated>
{
    public void Handle(InvoiceCreated message)
    {
    }

    // The Invoicing half of the same circle — see ReportEndpoint.
    public ReportEndpoint Reports;
}
