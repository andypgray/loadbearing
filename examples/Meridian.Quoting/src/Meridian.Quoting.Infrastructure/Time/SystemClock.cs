using Meridian.Quoting.Domain;

namespace Meridian.Quoting.Infrastructure.Time;

/// <summary>
///     The one place in the subsystem that reads the wall clock. Everything else takes
///     <see cref="IClock" /> and treats the current instant as an injected input.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}