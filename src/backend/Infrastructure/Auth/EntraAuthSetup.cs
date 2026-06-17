using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace AcaAspireAiTemplate.Backend.Infrastructure.Auth;

public static class EntraAuthSetup
{
    public const string ApiScopePolicyName = "ApiScope";
    private const string ApiScopeClaimValue = "access_as_user";
    private const string ScopeClaimType = "scp";
    private const string MappedScopeClaimType = "http://schemas.microsoft.com/identity/claims/scope";

    public static EntraAuthOptions ResolveEntraAuthOptions(IConfiguration configuration)
    {
        var environmentName = (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? "Production").Trim();
        var isProduction = string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);

        var authEnabledText = Environment.GetEnvironmentVariable("ENTRA_AUTH_ENABLED")
            ?? configuration["EntraAuth:Enabled"]
            ?? "true";
        var authEnabled = !string.Equals(authEnabledText, "false", StringComparison.OrdinalIgnoreCase);

        var tenantId = (Environment.GetEnvironmentVariable("ENTRA_TENANT_ID")
            ?? configuration["EntraAuth:TenantId"]
            ?? string.Empty).Trim();
        var apiClientId = (Environment.GetEnvironmentVariable("ENTRA_API_CLIENT_ID")
            ?? configuration["EntraAuth:ApiClientId"]
            ?? string.Empty).Trim();
        var authority = (Environment.GetEnvironmentVariable("ENTRA_AUTHORITY")
            ?? configuration["EntraAuth:Authority"]
            ?? string.Empty).Trim();
        var audience = (Environment.GetEnvironmentVariable("ENTRA_AUDIENCE")
            ?? configuration["EntraAuth:Audience"]
            ?? string.Empty).Trim();

        if (!authEnabled)
        {
            if (isProduction)
            {
                throw new InvalidOperationException(
                    "ENTRA_AUTH_ENABLED=false is not allowed in Production. "
                    + "Enable Entra auth before running this backend in production.");
            }

            Console.Error.WriteLine(
                $"WARNING: Entra auth disabled for environment '{environmentName}'. "
                + "Backend endpoints may be accessible without authentication.");
            return new EntraAuthOptions(false, tenantId, authority, apiClientId, audience);
        }

        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(tenantId)) issues.Add("ENTRA_TENANT_ID is missing");
        if (string.IsNullOrWhiteSpace(apiClientId)) issues.Add("ENTRA_API_CLIENT_ID is missing");

        var resolvedAuthority = string.IsNullOrWhiteSpace(authority)
            ? $"https://login.microsoftonline.com/{tenantId}/v2.0"
            : authority;
        var resolvedAudience = string.IsNullOrWhiteSpace(audience)
            ? $"api://{apiClientId}"
            : audience;

        if (!string.IsNullOrWhiteSpace(resolvedAuthority)
            && !Uri.TryCreate(resolvedAuthority, UriKind.Absolute, out _))
        {
            issues.Add("ENTRA_AUTHORITY must be a valid absolute URI");
        }

        if (issues.Count > 0)
        {
            if (!isProduction)
            {
                Console.Error.WriteLine(
                    $"WARNING: Entra auth was requested but configuration is incomplete in '{environmentName}': {string.Join("; ", issues)}. "
                    + "Falling back to ENTRA_AUTH_ENABLED=false behavior for local/dev startup.");
                return new EntraAuthOptions(false, tenantId, authority, apiClientId, audience);
            }

            throw new InvalidOperationException(
                "Entra authentication configuration is invalid: "
                + string.Join("; ", issues)
                + ". Configure ENTRA_* environment variables in Aspire/.env or deployment settings.");
        }

        return new EntraAuthOptions(
            true,
            tenantId,
            resolvedAuthority,
            apiClientId,
            resolvedAudience);
    }

    public static IServiceCollection AddEntraAuth(this IServiceCollection services, EntraAuthOptions options)
    {
        if (!options.Enabled)
        {
            services.AddAuthorization(authOptions =>
            {
                authOptions.AddPolicy(ApiScopePolicyName, policy =>
                    policy.RequireAssertion(_ => true));
            });
            return services;
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwtOptions =>
            {
                jwtOptions.Authority = options.Authority;
                jwtOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers =
                    [
                        $"https://login.microsoftonline.com/{options.TenantId}/v2.0",
                        $"https://sts.windows.net/{options.TenantId}/"
                    ],
                    ValidateAudience = true,
                    ValidAudiences =
                    [
                        options.Audience,
                        options.ApiClientId,
                        $"api://{options.ApiClientId}"
                    ],
                    ValidateLifetime = true
                };
                jwtOptions.RequireHttpsMetadata = true;
            });

        services.AddAuthorization(authOptions =>
        {
            authOptions.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            authOptions.AddPolicy(ApiScopePolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                {
                    var scopeClaims = context.User.FindAll(ScopeClaimType)
                        .Concat(context.User.FindAll(MappedScopeClaimType));

                    foreach (var scopeClaim in scopeClaims)
                    {
                        var scopes = scopeClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (scopes.Contains(ApiScopeClaimValue, StringComparer.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    if (context.User.HasClaim("roles", ApiScopeClaimValue)
                        || context.User.HasClaim(ClaimTypes.Role, ApiScopeClaimValue))
                    {
                        return true;
                    }

                    return false;
                });
            });
        });

        return services;
    }
}
