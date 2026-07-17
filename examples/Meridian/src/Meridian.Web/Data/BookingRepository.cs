using Meridian.Domain;
using Microsoft.Data.SqlClient;

namespace Meridian.Web.Data;

internal sealed class BookingRepository(IConfiguration configuration) : IBookingRepository
{
    public async Task Add(Booking booking)
    {
        string connectionString = configuration.GetConnectionString("Meridian")!;
        const string sql =
            """
            INSERT INTO Bookings (Reference, CustomerName, Lane, ContainerNumbers, CutoffUtc)
            VALUES (@reference, @customerName, @lane, @containerNumbers, @cutoffUtc)
            """;

        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@reference", booking.Reference);
        command.Parameters.AddWithValue("@customerName", booking.CustomerName);
        command.Parameters.AddWithValue("@lane", booking.Lane);
        command.Parameters.AddWithValue("@containerNumbers", string.Join(',', booking.ContainerNumbers));
        command.Parameters.AddWithValue("@cutoffUtc", booking.CutoffUtc);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    public async Task<Booking?> Get(string reference)
    {
        string connectionString = configuration.GetConnectionString("Meridian")!;
        const string sql =
            """
            SELECT Reference, CustomerName, Lane, ContainerNumbers, CutoffUtc
            FROM Bookings
            WHERE Reference = @reference
            """;

        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@reference", reference);

        await connection.OpenAsync();
        using SqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new Booking
        {
            Reference = reader.GetString(0),
            CustomerName = reader.GetString(1),
            Lane = reader.GetString(2),
            ContainerNumbers = reader.GetString(3).Split(','),
            CutoffUtc = reader.GetDateTime(4)
        };
    }
}