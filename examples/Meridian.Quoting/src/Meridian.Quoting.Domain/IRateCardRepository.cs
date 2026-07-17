namespace Meridian.Quoting.Domain;

/// <summary>
///     Persistence port for rate cards. The Domain owns the port; the Infrastructure layer implements it.
/// </summary>
public interface IRateCardRepository
{
    /// <summary>Returns the rate card effective for the lane at the given UTC instant, or null if none applies.</summary>
    Task<RateCard?> GetForLane(string lane, DateTime asOfUtc);
}