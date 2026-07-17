namespace Meridian.Operations.Invoicing.Contracts;

/// <summary>Registers the invoicing module's services.</summary>
public static class InvoicingModuleRegistration
{
    /// <summary>Adds the invoice run and its assembler and reconciler to the container.</summary>
    public static IServiceCollection AddInvoicingModule(this IServiceCollection services)
    {
        services.AddSingleton<InvoiceAssembler>();
        services.AddSingleton<DemurrageReconciler>();
        services.AddSingleton<IInvoiceRun, InvoiceRun>();
        return services;
    }
}