using Meridian.Domain;

namespace Meridian.Web.Data;

internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}