namespace MyApp.Web;

// A singleton (registered via AddSingleton in ServiceWiring) whose primary constructor captures a scoped
// IOrderFeed and a transient IOrderFormatter. Both outlive their intended lifetime once a singleton holds
// them — the two captive edges di/no-captive-dependencies bans (GRAMMAR 4.7).
public sealed class ReportScheduler(
    IOrderFeed feed,
    IOrderFormatter formatter)
{
    public string BuildReport()
    {
        return formatter.Format(feed.NextOrder());
    }
}
