namespace Meridian.Web.Models;

public sealed record CreateBookingRequest(
    string Reference,
    string CustomerName,
    string Lane,
    IReadOnlyList<string> ContainerNumbers,
    DateTime CutoffUtc);