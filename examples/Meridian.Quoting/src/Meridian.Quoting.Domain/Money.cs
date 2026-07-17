namespace Meridian.Quoting.Domain;

/// <summary>
///     A money amount in a single currency. Immutable value object; scaling by a whole
///     count (for example, a per-TEU rate times a container count) yields a new instance.
/// </summary>
public sealed record Money(decimal Amount, string Currency)
{
    /// <summary>Scales the amount by an integer factor, keeping the currency.</summary>
    public static Money operator *(Money money, int factor)
    {
        return money with { Amount = money.Amount * factor };
    }
}