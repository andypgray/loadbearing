namespace MyApp.Domain;

// A retry budget the domain applies around flaky work. Its broad catch names what it expects in a `when`
// filter, which is the house style exceptions/no-unfiltered-catch sanctions — so this is that
// rule's GREEN half, beside ReportEndpoint's unfiltered twin in the Web layer, which is its red one.
// Deliberately inert otherwise: no throw, no Web reference, no `new`, no registration, and the filter
// compares two parameters rather than reading a member. The only edges this file mints are the reference
// MyApp.Domain.RetryPolicy -> System.Exception (from the catch clause's type name) and the filtered catch
// edge beside it.
public class RetryPolicy
{
    public int NextAttempt(int attempt, int budget)
    {
        try
        {
            return attempt + 1;
        }
        catch (System.Exception) when (attempt < budget)
        {
            return -1;
        }
    }
}
