using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using AcaAspireAiTemplate.Backend.Infrastructure.Auth;

namespace AcaAspireAiTemplate.Backend.Features.Customers;

public static class Endpoint
{
    public static void MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/customers", async (ICustomerRepository repository) =>
        {
            var customers = await repository.GetCustomersAsync();
            return Results.Ok(customers);
        }).RequireAuthorization(EntraAuthSetup.ApiScopePolicyName);

        app.MapPost("/v1/customers", async (CreateCustomerRequest request, ICustomerRepository repository) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.City)
                || string.IsNullOrWhiteSpace(request.Status))
            {
                return Results.BadRequest("Name, email, city, and status are required.");
            }

            var id = await repository.CreateCustomerAsync(request);
            return Results.Created($"/v1/customers/{id}", new { id });
        }).RequireAuthorization(EntraAuthSetup.ApiScopePolicyName);

        app.MapPut("/v1/customers/{id:int}", async (int id, UpdateCustomerRequest request, ICustomerRepository repository) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.City)
                || string.IsNullOrWhiteSpace(request.Status))
            {
                return Results.BadRequest("Name, email, city, and status are required.");
            }

            var updated = await repository.UpdateCustomerAsync(id, request);
            return updated ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization(EntraAuthSetup.ApiScopePolicyName);

        app.MapDelete("/v1/customers/{id:int}", async (int id, ICustomerRepository repository) =>
        {
            var deleted = await repository.DeleteCustomerAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization(EntraAuthSetup.ApiScopePolicyName);
    }
}

public sealed record CustomerRecord(int Id, string Name, string Email, string City, string Status);
public sealed record CreateCustomerRequest(string Name, string Email, string City, string Status);
public sealed record UpdateCustomerRequest(string Name, string Email, string City, string Status);
