using Meridian.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Meridian.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class DriversController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("dispatch")]
    public IActionResult GetDispatchBoard()
    {
        string connectionString = configuration.GetConnectionString("Meridian")!;
        DateTime today = DateTime.Now.Date;

        const string sql =
            """
            SELECT Reference, CustomerName, Lane, ContainerNumbers, CutoffUtc
            FROM Bookings
            WHERE CAST(CutoffUtc AS date) = @today
            ORDER BY CutoffUtc
            """;

        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@today", today);
        connection.Open();

        List<Booking> dispatch = [];
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
            dispatch.Add(booking);
        }

        return Ok(dispatch);
    }
}