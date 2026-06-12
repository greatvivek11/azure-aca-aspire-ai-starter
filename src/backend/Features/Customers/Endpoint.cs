using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;

namespace AIHub.Backend.Features.Customers;

public static class Endpoint
{
    public static void MapCustomerEndpoints(this IEndpointRouteBuilder app, string sqlConnectionString)
    {
        app.MapGet("/v1/customers", async () =>
        {
            var customers = await GetCustomersAsync(sqlConnectionString);
            return Results.Ok(customers);
        });

        app.MapPost("/v1/customers", async (CreateCustomerRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.City)
                || string.IsNullOrWhiteSpace(request.Status))
            {
                return Results.BadRequest("Name, email, city, and status are required.");
            }

            var id = await CreateCustomerAsync(sqlConnectionString, request);
            return Results.Created($"/v1/customers/{id}", new { id });
        });

        app.MapPut("/v1/customers/{id:int}", async (int id, UpdateCustomerRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.City)
                || string.IsNullOrWhiteSpace(request.Status))
            {
                return Results.BadRequest("Name, email, city, and status are required.");
            }

            var updated = await UpdateCustomerAsync(sqlConnectionString, id, request);
            return updated ? Results.NoContent() : Results.NotFound();
        });

        app.MapDelete("/v1/customers/{id:int}", async (int id) =>
        {
            var deleted = await DeleteCustomerAsync(sqlConnectionString, id);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }

    private static async Task<List<CustomerRecord>> GetCustomersAsync(string connectionString)
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

    private static async Task<int> CreateCustomerAsync(string connectionString, CreateCustomerRequest request)
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

    private static async Task<bool> UpdateCustomerAsync(string connectionString, int id, UpdateCustomerRequest request)
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

    private static async Task<bool> DeleteCustomerAsync(string connectionString, int id)
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

internal sealed record CustomerRecord(int Id, string Name, string Email, string City, string Status);
internal sealed record CreateCustomerRequest(string Name, string Email, string City, string Status);
internal sealed record UpdateCustomerRequest(string Name, string Email, string City, string Status);
