namespace Meridian.Domain;

public interface IRateCardRepository
{
    Task<RateCard?> GetForLane(string lane, DateTime asOfUtc);
}