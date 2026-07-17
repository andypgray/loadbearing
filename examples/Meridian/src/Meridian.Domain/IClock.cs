namespace Meridian.Domain;

public interface IClock
{
    DateTime UtcNow { get; }
}
