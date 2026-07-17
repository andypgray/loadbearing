using Meridian.Domain;
using Microsoft.Data.SqlClient;

namespace Meridian.Web.Data;

internal sealed class RateCardRepository(IConfiguration configuration) : IRateCardRepository
{
    public async Task<RateCard?> GetForLane(string lane, DateTime asOfUtc)
    {
        string connectionString = configuration.GetConnectionString("Meridian")!;
        const string sql =
            """
            SELECT Lane, RatePerTeuUsd, ValidFromUtc, ValidUntilUtc
            FROM RateCards
            WHERE Lane = @lane AND ValidFromUtc <= @asOf AND ValidUntilUtc > @asOf
            """;

        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@lane", lane);
        command.Parameters.AddWithValue("@asOf", asOfUtc);

        await connection.OpenAsync();
        using SqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new RateCard
        {
            Lane = reader.GetString(0),
            RatePerTeuUsd = reader.GetDecimal(1),
            ValidFromUtc = reader.GetDateTime(2),
            ValidUntilUtc = reader.GetDateTime(3)
        };
    }
}