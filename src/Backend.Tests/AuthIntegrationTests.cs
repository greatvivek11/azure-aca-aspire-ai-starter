using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AcaAspireAiTemplate.Backend.Features.Health;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AcaAspireAiTemplate.Backend.Tests.Architecture;

public class AuthIntegrationTests
{
    [Fact]
    public async Task Health_Endpoint_Should_Be_Anonymous()
    {
        await using var app = await BuildTestAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/v1/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Protected_Endpoint_Should_Return_401_Without_Auth()
    {
        await using var app = await BuildTestAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/v1/protected");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_Endpoint_Should_Return_200_With_Auth()
    {
        await using var app = await BuildTestAppAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        var response = await client.GetAsync("/v1/protected");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Scope_Protected_Endpoint_Should_Return_403_Without_Scope()
    {
        await using var app = await BuildTestAppAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        var response = await client.GetAsync("/v1/scope-protected");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Scope_Protected_Endpoint_Should_Return_200_With_Scope()
    {
        await using var app = await BuildTestAppAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");
        client.DefaultRequestHeaders.Add("x-test-scope", "access_as_user");

        var response = await client.GetAsync("/v1/scope-protected");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<WebApplication> BuildTestAppAsync()
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

        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy("ApiScope", policy =>
                policy.RequireClaim("scp", "access_as_user"));
        });

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthEndpoint();
        app.MapGet("/v1/protected", () => Results.Ok(new { ok = true }));
        app.MapGet("/v1/scope-protected", () => Results.Ok(new { ok = true }))
            .RequireAuthorization("ApiScope");

        await app.StartAsync();
        return app;
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("x-test-auth", out var authHeader)
                || !string.Equals(authHeader.ToString(), "true", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "test-user-id"),
                new(ClaimTypes.Name, "integration-user")
            };

            if (Request.Headers.TryGetValue("x-test-scope", out var scopeHeader)
                && !string.IsNullOrWhiteSpace(scopeHeader))
            {
                claims.Add(new Claim("scp", scopeHeader.ToString()));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
