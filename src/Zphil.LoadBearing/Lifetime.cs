namespace Zphil.LoadBearing;

/// <summary>
///     The lifetime a container registration was made with. Pass one to <c>arch.Registered</c> to
///     select the types registered at that lifetime, service and implementation types alike, as in
///     <c>arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped))</c>.
/// </summary>
// LoadBearing's own enum, never Microsoft.Extensions.DependencyInjection.ServiceLifetime: naming it
// ServiceLifetime would CS0104-collide with MEDI's type in any spec project that `using`s
// Microsoft.Extensions.DependencyInjection.
public enum Lifetime
{
    /// <summary>
    ///     One instance for the container's lifetime: what <c>AddSingleton</c> registers, and the lifetime
    ///     <c>AddHostedService</c> carries.
    /// </summary>
    Singleton,

    /// <summary>
    ///     One instance per scope: what <c>AddScoped</c> registers, and the lifetime <c>AddDbContext</c>
    ///     carries unless the call says otherwise.
    /// </summary>
    Scoped,

    /// <summary>
    ///     A new instance per resolution: what <c>AddTransient</c> registers, and the lifetime
    ///     <c>AddHttpClient</c> carries.
    /// </summary>
    Transient
}
