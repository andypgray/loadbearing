namespace Zphil.LoadBearing;

/// <summary>
///     The service lifetime a container registration was made with, selected by
///     <c>arch.Registered(Lifetime)</c> (GRAMMAR §4.7, §5.1). This is LoadBearing's own enum —
///     deliberately NOT <c>Microsoft.Extensions.DependencyInjection.ServiceLifetime</c>: naming it
///     <c>ServiceLifetime</c> would CS0104-collide with MEDI's type in a spec project that
///     <c>using</c>s <c>Microsoft.Extensions.DependencyInjection</c> (the guidance-pack spec does).
/// </summary>
public enum Lifetime
{
    /// <summary>A single instance for the container's lifetime (<c>AddSingleton</c>).</summary>
    Singleton,

    /// <summary>One instance per scope (<c>AddScoped</c>).</summary>
    Scoped,

    /// <summary>A new instance per resolution (<c>AddTransient</c>).</summary>
    Transient
}