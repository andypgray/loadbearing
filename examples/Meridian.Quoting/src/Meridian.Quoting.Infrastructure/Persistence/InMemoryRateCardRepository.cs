using Meridian.Quoting.Domain;

namespace Meridian.Quoting.Infrastructure.Persistence;

/// <summary>Implements the rate-card persistence port over <see cref="InMemoryDatabase" />.</summary>
public sealed class InMemoryRateCardRepository(InMemoryDatabase database) : IRateCardRepository
{
    public Task<RateCard?> GetForLane(string lane, DateTime asOfUtc)
    {
        return Task.FromResult(database.FindRateCard(lane, asOfUtc));
    }
}