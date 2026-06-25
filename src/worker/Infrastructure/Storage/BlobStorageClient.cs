using System.Net.Http.Headers;
using Azure.Storage.Blobs;

internal sealed class BlobStorageClient(
    WorkerRuntimeOptions runtimeOptions,
    AzureAuthenticator authenticator,
    IHttpClientFactory httpClientFactory)
{
    public async Task<Stream> OpenBlobReadStreamAsync(string blobName)
    {
        if (string.Equals(runtimeOptions.StorageAuthMode, "managed-identity", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(runtimeOptions.StorageAccountName))
        {
            try
            {
                var token = await authenticator.TryAcquireManagedIdentityTokenAsync(
                    "https://storage.azure.com",
                    runtimeOptions.ManagedIdentityClientId);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return await DownloadBlobWithManagedIdentityAsync(blobName, token);
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        var blobServiceClient = new BlobServiceClient(runtimeOptions.StorageConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(runtimeOptions.StorageContainerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        if (!await blobClient.ExistsAsync())
        {
            throw new InvalidOperationException($"Blob '{blobName}' was not found.");
        }

        return await blobClient.OpenReadAsync();
    }

    private async Task<Stream> DownloadBlobWithManagedIdentityAsync(string blobName, string bearerToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildBlobReadUrl(runtimeOptions.StorageAccountName, runtimeOptions.StorageContainerName, blobName));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Add("x-ms-version", "2023-11-03");

        using var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var sourceStream = await response.Content.ReadAsStreamAsync();
        var buffer = new MemoryStream();
        await sourceStream.CopyToAsync(buffer);
        buffer.Position = 0;
        return buffer;
    }

    private static string BuildBlobReadUrl(string storageAccountName, string storageContainerName, string blobName)
    {
        var escapedBlobPath = string.Join(
            "/",
            blobName.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return $"https://{storageAccountName}.blob.core.windows.net/{Uri.EscapeDataString(storageContainerName)}/{escapedBlobPath}";
    }
}
