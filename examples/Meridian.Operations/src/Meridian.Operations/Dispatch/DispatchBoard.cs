using Meridian.Operations.Dispatch.Contracts;
using Meridian.Operations.Tracking.Contracts;

namespace Meridian.Operations.Dispatch;

/// <summary>
///     Assigns drivers and trucks to haulage legs. It consults the tracking log first: a leg is
///     only dispatched once the shipment carries a Booked milestone, which is dispatch's one
///     legitimate reach into another module's contract.
/// </summary>
internal sealed class DispatchBoard(ITrackingLog tracking, DriverRoster roster) : IDispatchBoard
{
    private readonly List<Assignment> assignments = [];

    public AssignmentView Assign(string shipmentId, string origin, string destination)
    {
        bool booked = tracking.MilestonesFor(shipmentId).Any(milestone => milestone.Kind == MilestoneKind.Booked);
        if (!booked)
            throw new InvalidOperationException(
                $"Shipment {shipmentId} has no Booked milestone; a haulage leg cannot be dispatched before the booking is confirmed.");

        int sequence = assignments.Count(assignment => assignment.Leg.ShipmentId == shipmentId) + 1;
        var leg = new HaulageLeg(shipmentId, sequence, origin, destination);
        (string driver, string truck) = roster.Next();
        var assignment = new Assignment(leg, driver, truck);
        assignments.Add(assignment);

        return ToView(assignment);
    }

    public IReadOnlyList<AssignmentView> ListAssignments(string shipmentId)
    {
        return assignments
            .Where(assignment => assignment.Leg.ShipmentId == shipmentId)
            .Select(ToView)
            .ToList();
    }

    private static AssignmentView ToView(Assignment assignment)
    {
        HaulageLeg leg = assignment.Leg;
        return new AssignmentView(leg.ShipmentId, leg.Sequence, leg.Origin, leg.Destination, assignment.Driver, assignment.Truck);
    }
}