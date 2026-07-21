namespace MyApp.Web;

// A transient dependency (registered via AddTransient in ServiceWiring) that a singleton must not capture.
public interface IOrderFormatter
{
    string Format(string order);
}
