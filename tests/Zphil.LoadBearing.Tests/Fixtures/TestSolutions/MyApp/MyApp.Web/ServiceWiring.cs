using Microsoft.Extensions.DependencyInjection;

namespace MyApp.Web;

// The composition root: registers the report scheduler as a singleton and the two dependencies it captures
// at their shorter lifetimes (scoped IOrderFeed, transient IOrderFormatter). A static class has no instance
// constructor, so it declares no injection edges of its own — it only contributes the three registration
// facts di/no-captive-dependencies reads (GRAMMAR 4.7).
public static class ServiceWiring
{
    public static void Configure(IServiceCollection services)
    {
        services.AddSingleton<ReportScheduler>();
        services.AddScoped<IOrderFeed, OrderFeed>();
        services.AddTransient<IOrderFormatter, OrderFormatter>();
    }
}
