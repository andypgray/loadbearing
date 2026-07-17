using Meridian.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Meridian.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ShipmentsController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public IActionResult GetArrivals()
    {
        string connectionString = configuration.GetConnectionString("Meridian")!;
        DateTime asOfUtc = DateTime.UtcNow;

        const string sql =
            """
            SELECT Reference, CustomerName, Lane, ContainerNumbers, CutoffUtc
            FROM Bookings
            WHERE CutoffUtc >= @asOf
            ORDER BY CutoffUtc
            """;

        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@asOf", asOfUtc);
        connection.Open();

        List<Booking> arrivals = [];
        using SqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            var booking = new Booking
            {
                Reference = reader.GetString(0),
                CustomerName = reader.GetString(1),
                Lane = reader.GetString(2),
                ContainerNumbers = reader.GetString(3).Split(','),
                CutoffUtc = reader.GetDateTime(4)
            };
            arrivals.Add(booking);
        }

        return Ok(arrivals);
    }
}