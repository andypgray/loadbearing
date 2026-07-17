namespace Meridian.Operations.Dispatch.Contracts;

/// <summary>Registers the dispatch module's services.</summary>
public static class DispatchModuleRegistration
{
    /// <summary>Adds the dispatch board and its driver roster to the container.</summary>
    public static IServiceCollection AddDispatchModule(this IServiceCollection services)
    {
        services.AddSingleton<DriverRoster>();
        services.AddSingleton<IDispatchBoard, DispatchBoard>();
        return services;
    }
}