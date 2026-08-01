using System.Data.SqlClient;

namespace Classic.Billing
{
    // Seeded violation: inline ADO.NET, the pattern the classic codebase is full of.
    public class BillingCalculator
    {
        public decimal Total(string account)
        {
            using (var connection = new SqlConnection("Server=.;Database=Billing;Integrated Security=true"))
            {
                connection.Open();
                using (var command = new SqlCommand("select sum(Amount) from Charges where Account = @account", connection))
                {
                    command.Parameters.AddWithValue("@account", account);
                    return (decimal)command.ExecuteScalar();
                }
            }
        }
    }
}
