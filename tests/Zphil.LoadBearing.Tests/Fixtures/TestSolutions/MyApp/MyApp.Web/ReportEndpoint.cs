namespace MyApp.Web;

// A request/report endpoint helper that runs its work behind a blanket try/catch and swallows every
// exception — the legacy swallow-and-continue handler pattern. Spelling no `when` filter, the
// catch (System.Exception) is the red site for BOTH exceptions/no-general-catch and exceptions/no-unfiltered-catch.
// Nothing else trips a rule: not a *Controller name, no System.Data, no clock read, no Task method, no Domain ref.
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
