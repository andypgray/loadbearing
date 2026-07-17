using Meridian.Operations.Demurrage;
using Meridian.Operations.Dispatch.Contracts;
using Meridian.Operations.Invoicing.Contracts;
using Meridian.Operations.Tracking.Contracts;

namespace Meridian.Operations.Host;

/// <summary>
///     Wires the modular monolith together: each module contributes its own registration
///     extension, and the host adds the demurrage calculator facade directly.
/// </summary>
internal static class OperationsBootstrap
{
    /// <summary>Registers every module's services on the container.</summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddTrackingModule();
        services.AddDispatchModule();
        services.AddInvoicingModule();

        // Demurrage exposes only its calculator facade, so it has no module-registration
        // extension; the host binds the interface to its single implementation here.
        services.AddSingleton<IDemurrageCalculator, DemurrageCalculator>();
    }
}