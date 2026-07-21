using Meridian.Interchange.Configuration;
using Meridian.Interchange.Dispatch;
using Meridian.Interchange.Outbox;
using Meridian.Interchange.Partners;
using Meridian.Interchange.Processing;
using Microsoft.Extensions.Options;

namespace Meridian.Interchange.Host;

/// <summary>
///     The interchange subsystem's composition root: it binds options, wires each partner client to
///     a pooled HttpClient from the factory, and registers the outbox pipeline and the dispatcher.
///     This is the one place that constructs a partner client or resolves a service from the provider.
/// </summary>
public static class InterchangeServiceCollectionExtensions
{
    /// <summary>Registers the interchange worker's services on the container.</summary>
    public static IServiceCollection AddInterchange(this IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Interchange");
        services.Configure<InterchangeOptions>(section);

        PartnerEndpoints endpoints = (section.Get<InterchangeOptions>() ?? new InterchangeOptions()).Partners;

        // Typed clients take their pooled HttpClient from the factory. All three share the
        // IPartnerClient service type, so each registration gets a distinct logical client name to
        // keep the factory's named configurations from colliding.
        services.AddHttpClient<IPartnerClient, CarrierClient>("carrier", http => http.BaseAddress = new Uri(endpoints.Carrier));
        services.AddHttpClient<IPartnerClient, CustomsFilingClient>("customs", http => http.BaseAddress = new Uri(endpoints.Customs));
        services.AddHttpClient<IPartnerClient, TerminalClient>("terminal", http => http.BaseAddress = new Uri(endpoints.Terminal));

        // The legacy adapter is constructed by hand so it can be handed a named client from the
        // factory. This factory lambda is the subsystem's one sanctioned construct-and-resolve site.
        services.AddHttpClient("legacy", http => http.BaseAddress = new Uri(endpoints.LegacyManifest));
        services.AddScoped<IPartnerClient>(sp => new LegacyManifestClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("legacy"),
            sp.GetRequiredService<IOptions<InterchangeOptions>>()));

        services.AddScoped<IOutboxStore, OutboxStore>();
        services.AddScoped<IOutboxProcessor, OutboxProcessor>();

        services.AddSingleton<ScopedDispatchRunner>();
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}