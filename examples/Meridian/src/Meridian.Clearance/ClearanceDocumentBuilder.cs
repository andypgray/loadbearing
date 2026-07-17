using System.Text;

namespace Meridian.Clearance;

internal sealed class ClearanceDocumentBuilder
{
    public string Build(string bookingReference, IReadOnlyCollection<string> containerNumbers)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MERIDIAN FREIGHT - CUSTOMS CLEARANCE DECLARATION");
        builder.AppendLine($"Booking: {bookingReference}");
        builder.AppendLine("Containers declared:");

        var line = 1;
        foreach (string containerNumber in containerNumbers)
        {
            builder.AppendLine($"  {line:D3}  {containerNumber}");
            line++;
        }

        builder.AppendLine($"Total containers: {containerNumbers.Count}");
        return builder.ToString();
    }
}