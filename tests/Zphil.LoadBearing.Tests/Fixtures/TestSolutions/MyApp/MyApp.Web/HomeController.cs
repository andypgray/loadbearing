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

    // New async-style methods (Phase 14 member subjects): Save/Load return a Task form without the
    // Async suffix — red under naming/async-suffix; Load's Task<int> proves open-generic matching
    // end-to-end. SaveAsync is correctly named — green.
    public System.Threading.Tasks.Task Save()
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<int> Load()
    {
        return System.Threading.Tasks.Task.FromResult(0);
    }

    public System.Threading.Tasks.Task SaveAsync()
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
