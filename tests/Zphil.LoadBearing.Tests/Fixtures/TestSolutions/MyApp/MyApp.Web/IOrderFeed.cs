namespace MyApp.Web;

// A scoped dependency (registered via AddScoped in ServiceWiring) that a singleton must not capture.
public interface IOrderFeed
{
    string NextOrder();
}
