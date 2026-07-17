namespace Meridian.Operations.Dispatch;

/// <summary>In-memory pool of available drivers and trucks, handed out round-robin.</summary>
internal sealed class DriverRoster
{
    private readonly string[] drivers = ["Ana Reyes", "Ben Okafor", "Chloe Nguyen"];
    private readonly string[] trucks = ["T-101", "T-102", "T-103"];
    private int next;

    /// <summary>Returns the next available driver and truck, cycling back to the start of the pool.</summary>
    public (string Driver, string Truck) Next()
    {
        (string driver, string truck) = (drivers[next], trucks[next]);
        next = (next + 1) % drivers.Length;
        return (driver, truck);
    }
}