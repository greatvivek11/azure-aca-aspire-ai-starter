using System.Net;
using AcaAspireAiTemplate.Backend.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace AcaAspireAiTemplate.Backend.Tests.Architecture;

[Collection("EnvironmentVariableTests")]
public sealed class ProgramPipelineIntegrationTests
{
    [Fact]
    public async Task Health_Should_Be_Anonymous_In_Real_Program_Pipeline()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Protected_Customers_Should_Be_401_Without_Token_In_Real_Program_Pipeline()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/customers");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiScope_Endpoint_Should_Stay_Reachable_When_Entra_Auth_Is_Disabled()
    {
        await using var app = await BuildDisabledAuthTestAppAsync();
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/v1/scoped");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("BACKEND_SKIP_STARTUP_TASKS_FOR_TESTS", "true");
        Environment.SetEnvironmentVariable("AI_MODE", "local");
        Environment.SetEnvironmentVariable("ConnectionStrings__SqlServer", "Server=localhost;Database=test;User Id=test;Password=test;");
        Environment.SetEnvironmentVariable("ENTRA_AUTH_ENABLED", "true");
        Environment.SetEnvironmentVariable("ENTRA_TENANT_ID", "11111111-1111-1111-1111-111111111111");
        Environment.SetEnvironmentVariable("ENTRA_API_CLIENT_ID", "22222222-2222-2222-2222-222222222222");
        Environment.SetEnvironmentVariable("ENTRA_AUTHORITY", "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0");
        Environment.SetEnvironmentVariable("ENTRA_AUDIENCE", "api://22222222-2222-2222-2222-222222222222");

        return new WebApplicationFactory<Program>();
    }

    private static async Task<WebApplication> BuildDisabledAuthTestAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Services
            .AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                _ => { });

        builder.Services.AddAuthorization();
        builder.Services.AddEntraAuth(new EntraAuthOptions(false, string.Empty, string.Empty, string.Empty, string.Empty));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/v1/scoped", () => Results.Ok(new { ok = true }))
            .RequireAuthorization(EntraAuthSetup.ApiScopePolicyName);

        await app.StartAsync();
        return app;
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthHandler(
            Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}
