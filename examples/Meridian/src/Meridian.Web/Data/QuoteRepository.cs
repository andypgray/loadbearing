using Meridian.Domain;
using Microsoft.Data.SqlClient;

namespace Meridian.Web.Data;

internal sealed class QuoteRepository(IConfiguration configuration) : IQuoteRepository
{
    public async Task Add(Quote quote)
    {
        string connectionString = configuration.GetConnectionString("Meridian")!;
        const string sql =
            """
            INSERT INTO Quotes (Reference, Lane, CustomerName, TeuCount, AmountUsd, ExpiresUtc)
            VALUES (@reference, @lane, @customerName, @teuCount, @amountUsd, @expiresUtc)
            """;

        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@reference", quote.Reference);
        command.Parameters.AddWithValue("@lane", quote.Lane);
        command.Parameters.AddWithValue("@customerName", quote.CustomerName);
        command.Parameters.AddWithValue("@teuCount", quote.TeuCount);
        command.Parameters.AddWithValue("@amountUsd", quote.AmountUsd);
        command.Parameters.AddWithValue("@expiresUtc", quote.ExpiresUtc);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    public async Task<Quote?> Get(string reference)
    {
        string connectionString = configuration.GetConnectionString("Meridian")!;
        const string sql =
            """
            SELECT Reference, Lane, CustomerName, TeuCount, AmountUsd, ExpiresUtc
            FROM Quotes
            WHERE Reference = @reference
            """;

        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@reference", reference);

        await connection.OpenAsync();
        using SqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new Quote
        {
            Reference = reader.GetString(0),
            Lane = reader.GetString(1),
            CustomerName = reader.GetString(2),
            TeuCount = reader.GetInt32(3),
            AmountUsd = reader.GetDecimal(4),
            ExpiresUtc = reader.GetDateTime(5)
        };
    }
}