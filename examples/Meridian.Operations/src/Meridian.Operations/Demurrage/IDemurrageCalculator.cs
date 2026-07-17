namespace Meridian.Operations.Demurrage;

/// <summary>
///     The demurrage module's only sanctioned entry point. Given a container and its discharge
///     and gate-out dates, it returns the total demurrage charge; the per-day breakdown behind
///     the figure stays inside the module.
/// </summary>
public interface IDemurrageCalculator
{
    /// <summary>Returns the total demurrage charge for a container across its free-time and billable days.</summary>
    decimal CalculateCharge(string containerId, DateOnly dischargedAt, DateOnly gatedOutAt);
}