namespace Meridian.Operations.Dispatch.Contracts;

/// <summary>Facade for assigning drivers and trucks to a shipment's haulage legs.</summary>
public interface IDispatchBoard
{
    /// <summary>Assigns a driver and truck to a new haulage leg for a booked shipment.</summary>
    AssignmentView Assign(string shipmentId, string origin, string destination);

    /// <summary>Returns the assignments made for a shipment, in the order they were assigned.</summary>
    IReadOnlyList<AssignmentView> ListAssignments(string shipmentId);
}