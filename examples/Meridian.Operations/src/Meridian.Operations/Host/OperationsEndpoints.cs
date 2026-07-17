using Meridian.Operations.Dispatch.Contracts;
using Meridian.Operations.Invoicing.Contracts;
using Meridian.Operations.Tracking.Contracts;

namespace Meridian.Operations.Host;

/// <summary>Maps the operations HTTP endpoints onto the module facades.</summary>
internal static class OperationsEndpoints
{
    /// <summary>Registers the minimal-API endpoints for dispatch, tracking, and invoicing.</summary>
    public static void Map(WebApplication app)
    {
        app.MapPost("/shipments/{shipmentId}/assign", AssignLeg);
        app.MapPost("/shipments/{shipmentId}/milestones", RecordMilestone);
        app.MapGet("/shipments/{shipmentId}/milestones", ListMilestones);
        app.MapPost("/shipments/{shipmentId}/invoice", GenerateInvoice);
    }

    private static IResult AssignLeg(string shipmentId, string origin, string destination, IDispatchBoard board)
    {
        try
        {
            AssignmentView assignment = board.Assign(shipmentId, origin, destination);
            return Results.Ok(assignment);
        }
        catch (InvalidOperationException ex)
        {
            return Results.UnprocessableEntity(ex.Message);
        }
    }

    private static IResult RecordMilestone(string shipmentId, MilestoneKind kind, DateOnly at, ITrackingLog tracking)
    {
        var milestone = new ShipmentMilestone(shipmentId, kind, at);
        tracking.Record(milestone);
        return Results.Ok(milestone);
    }

    private static IResult ListMilestones(string shipmentId, ITrackingLog tracking)
    {
        return Results.Ok(tracking.MilestonesFor(shipmentId));
    }

    private static IResult GenerateInvoice(string shipmentId, IInvoiceRun invoices)
    {
        return Results.Ok(invoices.GenerateFor(shipmentId));
    }
}