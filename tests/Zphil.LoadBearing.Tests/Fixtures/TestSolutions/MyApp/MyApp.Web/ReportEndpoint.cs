namespace MyApp.Web;

// A request/report endpoint helper that runs its work behind a blanket try/catch and swallows every
// exception — the legacy swallow-and-continue handler pattern. The catch (System.Exception) is the red
// site for exceptions/no-general-catch (rule 10). Nothing else here trips a rule: not a *Controller name,
// no System.Data, no ambient-clock read, no Task-returning method, no Domain or frozen-Billing reference.
public class ReportEndpoint
{
    public int RenderReport(int reportId)
    {
        try
        {
            return reportId * 2;
        }
        catch (System.Exception)
        {
            return -1;
        }
    }
}
