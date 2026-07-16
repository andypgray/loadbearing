using System.Text;
using MyApp.Legacy.Billing;

namespace MyApp.Web;

[WebRoute("/home")]
public class HomeController
{
    private readonly StringBuilder log = new StringBuilder();

    public string RenderOrder(string description)
    {
        log.Append(description);
        return log.ToString();
    }

    public decimal ShowInvoiceTotal(IBillingFacade facade)
    {
        log.Append("total requested");
        return facade.CalculateTotal();
    }

    // New code written in the OLD pattern: not in the baseline, so this stays red under the ratchet.
    public System.Data.DataTable ExportOrders()
    {
        return new System.Data.DataTable();
    }

    // New code reading the ambient clock directly: both reads are red under time/inject-clock.
    public System.DateTime ExportStamp()
    {
        return System.DateTime.Now;
    }

    public System.DateTime ExportStampUtc()
    {
        return System.DateTime.UtcNow;
    }
}
