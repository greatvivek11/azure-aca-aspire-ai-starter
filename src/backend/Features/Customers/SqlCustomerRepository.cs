using Microsoft.Data.SqlClient;

namespace AcaAspireAiTemplate.Backend.Features.Customers;

public sealed class SqlCustomerRepository(string connectionString) : ICustomerRepository
{
    public async Task<List<CustomerRecord>> GetCustomersAsync()
    {
        var customers = new List<CustomerRecord>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT Id, Name, Email, City, Status
FROM dbo.Customers
ORDER BY Id;
""";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            customers.Add(new CustomerRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return customers;
    }

    public async Task<int> CreateCustomerAsync(CreateCustomerRequest request)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO dbo.Customers (Name, Email, City, Status)
OUTPUT INSERTED.Id
VALUES (@name, @email, @city, @status);
""";
        command.Parameters.AddWithValue("@name", request.Name.Trim());
        command.Parameters.AddWithValue("@email", request.Email.Trim());
        command.Parameters.AddWithValue("@city", request.City.Trim());
        command.Parameters.AddWithValue("@status", request.Status.Trim());

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<bool> UpdateCustomerAsync(int id, UpdateCustomerRequest request)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE dbo.Customers
SET Name = @name,
    Email = @email,
    City = @city,
    Status = @status
WHERE Id = @id;
""";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@name", request.Name.Trim());
        command.Parameters.AddWithValue("@email", request.Email.Trim());
        command.Parameters.AddWithValue("@city", request.City.Trim());
        command.Parameters.AddWithValue("@status", request.Status.Trim());

        var affected = await command.ExecuteNonQueryAsync();
        return affected > 0;
    }

    public async Task<bool> DeleteCustomerAsync(int id)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM dbo.Customers WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id);

        var affected = await command.ExecuteNonQueryAsync();
        return affected > 0;
    }
}
