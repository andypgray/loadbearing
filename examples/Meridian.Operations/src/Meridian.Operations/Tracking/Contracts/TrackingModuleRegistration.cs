namespace Meridian.Operations.Tracking.Contracts;

/// <summary>Registers the tracking module's services.</summary>
public static class TrackingModuleRegistration
{
    /// <summary>Adds the tracking log and its milestone store to the container.</summary>
    public static IServiceCollection AddTrackingModule(this IServiceCollection services)
    {
        services.AddSingleton<MilestoneStore>();
        services.AddSingleton<ITrackingLog, TrackingLog>();
        return services;
    }
}