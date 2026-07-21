namespace MyApp.Web;

public sealed class OrderFeed : IOrderFeed
{
    public string NextOrder()
    {
        return "ORD-1";
    }
}
