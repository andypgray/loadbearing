using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Meridian.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class InvoicesController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("{bookingReference}")]
    public IActionResult GetInvoice(string bookingReference)
    {
        string connectionString = configuration.GetConnectionString("Meridian")!;

        const string sql =
            """
            SELECT CustomerName, Lane, ContainerNumbers
            FROM Bookings
            WHERE Reference = @reference
            """;

        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@reference", bookingReference);
        connection.Open();

        using SqlDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return NotFound();

        string customerName = reader.GetString(0);
        string lane = reader.GetString(1);
        int containerCount = reader.GetString(2).Split(',').Length;

        DateTime issueDate = DateTime.Now;
        DateTime dueDateUtc = DateTime.UtcNow.AddDays(30);

        var invoice =
            $"""
             INVOICE for booking {bookingReference}
             Customer: {customerName}
             Lane: {lane}
             Containers: {containerCount}
             Issued: {issueDate:yyyy-MM-dd}
             Payment due (UTC): {dueDateUtc:yyyy-MM-dd}
             """;

        return Ok(invoice);
    }
}