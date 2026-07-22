namespace MyApp.Domain;

// The domain's own exception type — the sanctioned thing an order rule may throw. Rule 11
// (exceptions/domain-throws-domain) permits only this; any BCL throw from the Domain layer is red.
public class OrderRuleViolation : System.Exception
{
    public OrderRuleViolation(string message) : base(message)
    {
    }
}
