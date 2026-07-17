namespace Meridian.Clearance;

public sealed class ClearanceGateway : IClearanceGateway
{
    private readonly ClearanceDocumentBuilder _documentBuilder = new();
    private readonly ContainerNumberValidator _validator = new();

    public bool IsValidContainerNumber(string containerNumber)
    {
        return _validator.IsValid(containerNumber);
    }

    public string BuildClearanceDocument(string bookingReference, IReadOnlyCollection<string> containerNumbers)
    {
        return _documentBuilder.Build(bookingReference, containerNumbers);
    }
}