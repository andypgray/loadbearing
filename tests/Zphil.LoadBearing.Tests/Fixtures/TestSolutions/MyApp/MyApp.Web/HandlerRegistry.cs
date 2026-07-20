namespace MyApp.Web;

// The sanctioned constructor: handler discovery resolves through this registry, so di/handlers-via-registry
// exempts it via .Except — its direct construction of a handler is legitimate, not a violation.
public class HandlerRegistry
{
    public IHandler<InvoiceCreated> Resolve()
    {
        return new InvoiceCreatedHandler();
    }
}
