using MyApp.Legacy.Billing;

namespace MyApp.Web;

public class InvoiceController
{
    public decimal Recalculate(decimal amount)
    {
        BillingCalculator calculator = new BillingCalculator();
        return calculator.RoundLineItem(amount, RoundingMode.Bankers);
    }

    // Legacy Active Record style: builds a DataTable inline (the Migrate rule grandfathers this site).
    public System.Data.DataTable ExportInvoices()
    {
        return new System.Data.DataTable();
    }
}
