using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Meridian.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ManifestsController(IConfiguration configuration) : ControllerBase
{
    [HttpPost("{bookingReference}/close")]
    public IActionResult CloseManifest(string bookingReference)
    {
        string connectionString = configuration.GetConnectionString("Meridian")!;
        DateTime closedUtc = DateTime.UtcNow;

        const string sql =
            """
            UPDATE Manifests
            SET ClosedUtc = @closedUtc
            WHERE BookingReference = @reference
            """;

        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@closedUtc", closedUtc);
        command.Parameters.AddWithValue("@reference", bookingReference);
        connection.Open();

        int rowsAffected = command.ExecuteNonQuery();
        if (rowsAffected == 0) return NotFound();

        return Ok(closedUtc);
    }
}