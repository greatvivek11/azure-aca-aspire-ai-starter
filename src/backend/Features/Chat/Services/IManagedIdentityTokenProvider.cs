namespace AcaAspireAiTemplate.Backend.Features.Chat;

internal interface IManagedIdentityTokenProvider
{
    Task<string> AcquireTokenAsync(string resource, string? managedIdentityClientId, CancellationToken cancellationToken = default);
    Task<string?> TryAcquireTokenAsync(string resource, string? managedIdentityClientId, CancellationToken cancellationToken = default);
}
