namespace MyApp.Domain;

// Applies order rules. Throwing the domain's own OrderRuleViolation is sanctioned (rule 11 green); the
// throw new System.InvalidOperationException(...) is a BCL throw — the red site for
// exceptions/domain-throws-domain (rule 11). No Web reference, so the layering rules stay clean.
public class OrderApproval
{
    public void Approve(Order order)
    {
        if (order.Total.Amount < 0)
        {
            throw new OrderRuleViolation("Order total must not be negative.");
        }

        if (!order.Validate())
        {
            throw new System.InvalidOperationException("Order failed validation.");
        }
    }
}
