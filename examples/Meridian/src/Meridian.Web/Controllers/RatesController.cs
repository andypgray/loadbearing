using Meridian.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Meridian.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class RatesController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public IActionResult GetActiveRates()
    {
        string connectionString = configuration.GetConnectionString("Meridian")!;
        DateTime nowUtc = DateTime.UtcNow;

        const string sql =
            """
            SELECT Lane, RatePerTeuUsd, ValidFromUtc, ValidUntilUtc
            FROM RateCards
            WHERE ValidFromUtc <= @now AND ValidUntilUtc > @now
            ORDER BY Lane
            """;

        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@now", nowUtc);
        connection.Open();

        List<RateCard> rates = [];
        using SqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            var rateCard = new RateCard
            {
                Lane = reader.GetString(0),
                RatePerTeuUsd = reader.GetDecimal(1),
                ValidFromUtc = reader.GetDateTime(2),
                ValidUntilUtc = reader.GetDateTime(3)
            };
            rates.Add(rateCard);
        }

        return Ok(rates);
    }
}