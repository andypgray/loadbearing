using Meridian.Operations.Tracking.Contracts;

namespace Meridian.Operations.Tracking;

/// <summary>
///     Records milestones into the <see cref="MilestoneStore" /> and, for the two milestones other
///     subsystems act on, emits a domain-event value.
/// </summary>
internal sealed class TrackingLog(MilestoneStore store, ILogger<TrackingLog> logger) : ITrackingLog
{
    public void Record(ShipmentMilestone milestone)
    {
        store.Add(milestone);
        EmitDomainEvent(milestone);
    }

    public IReadOnlyList<ShipmentMilestone> MilestonesFor(string shipmentId)
    {
        return store.For(shipmentId);
    }

    // Departed and Discharged are the milestones downstream subsystems react to, so tracking
    // projects them to domain-event values as they are recorded. Today the event is logged; a
    // later WP can publish the same value onto a bus without changing this call site.
    private void EmitDomainEvent(ShipmentMilestone milestone)
    {
        object? domainEvent = milestone.Kind switch
        {
            MilestoneKind.Departed => new ShipmentDepartedEvent(milestone.ShipmentId, milestone.At),
            MilestoneKind.Discharged => new ShipmentDischargedEvent(milestone.ShipmentId, milestone.At),
            _ => null
        };

        if (domainEvent is not null) logger.LogInformation("Tracking domain event: {DomainEvent}", domainEvent);
    }
}