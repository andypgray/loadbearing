namespace Meridian.Operations.Tracking.Contracts;

/// <summary>The lifecycle milestones a shipment passes through, in order.</summary>
public enum MilestoneKind
{
    /// <summary>The booking is confirmed. Dispatch will not assign haulage before this milestone.</summary>
    Booked,

    /// <summary>The shipment has left its origin port.</summary>
    Departed,

    /// <summary>The shipment has reached its destination port.</summary>
    Arrived,

    /// <summary>The container has been discharged from the vessel; the demurrage free-time clock starts.</summary>
    Discharged,

    /// <summary>The container has passed out of the terminal gate; the demurrage window closes.</summary>
    GateOut
}