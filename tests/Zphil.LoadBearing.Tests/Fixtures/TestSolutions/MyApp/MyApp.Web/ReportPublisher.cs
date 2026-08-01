namespace MyApp.Web;

// The rethrow-axis differentiator, beside ReportEndpoint's swallow. This publish helper wraps its work in the
// same blanket try/catch and spells no `when` filter either — but its clause ends in a bare `throw;`, so the
// failure travels on and nothing is suppressed. It is therefore red under exceptions/no-general-catch and
// exceptions/no-unfiltered-catch, which judge the catch, and GREEN under exceptions/no-swallowed-catch, which
// judges what the handler does with it. ReportEndpoint is red under all three.
// Nothing else trips a rule: not a *Controller name, no System.Data, no clock read, no Task method, no Domain ref.
public class ReportPublisher
{
    private int _attempts;

    public int Attempts => _attempts;

    public int Publish(int reportId)
    {
        _attempts++;
        try
        {
            return reportId * 3;
        }
        catch (System.Exception)
        {
            _attempts = 0;
            throw;
        }
    }
}
