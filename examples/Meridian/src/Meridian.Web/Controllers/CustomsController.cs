using Meridian.Clearance;
using Meridian.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Meridian.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class CustomsController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("pending")]
    public IActionResult GetPendingClearances()
    {
        string connectionString = configuration.GetConnectionString("Meridian")!;
        DateTime nowUtc = DateTime.UtcNow;

        const string sql =
            """
            SELECT Reference, CustomerName, Lane, ContainerNumbers, CutoffUtc
            FROM Bookings
            WHERE CutoffUtc <= @cutoff
            ORDER BY CutoffUtc
            """;

        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@cutoff", nowUtc);
        connection.Open();

        List<Booking> pending = [];
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
            pending.Add(booking);
        }

        return Ok(pending);
    }

    [HttpGet("container-check/{number}")]
    public IActionResult CheckContainer(string number)
    {
        var validator = new ContainerNumberValidator();
        bool isValid = validator.IsValid(number);
        return Ok(isValid);
    }
}