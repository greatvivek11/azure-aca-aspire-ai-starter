namespace AcaAspireAiTemplate.Backend.Infrastructure.Auth;

public sealed record EntraAuthOptions(
    bool Enabled,
    string TenantId,
    string Authority,
    string ApiClientId,
    string Audience);
