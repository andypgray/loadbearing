namespace Meridian.Clearance;

public interface IClearanceGateway
{
    bool IsValidContainerNumber(string containerNumber);

    string BuildClearanceDocument(string bookingReference, IReadOnlyCollection<string> containerNumbers);
}