namespace MyApp.Web;

// Bypasses HandlerRegistry and news up the handler directly — the di/handlers-via-registry demo red.
public class InvoiceService
{
    public IHandler<InvoiceCreated> CreateHandler()
    {
        return new InvoiceCreatedHandler();
    }
}
