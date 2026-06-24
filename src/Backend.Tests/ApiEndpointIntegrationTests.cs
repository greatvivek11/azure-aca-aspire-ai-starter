using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AcaAspireAiTemplate.Backend.Features.Chat;
using AcaAspireAiTemplate.Backend.Features.DocumentIngestion;
using AcaAspireAiTemplate.Backend.Infrastructure.Ai;
using AcaAspireAiTemplate.Backend.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AcaAspireAiTemplate.Backend.Tests.Architecture;

public class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task Chat_Should_Return_400_When_Message_Is_Missing()
    {
        await using var app = await BuildApiTestAppAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        using var response = await client.PostAsync(
            "/v1/chat",
            new StringContent("{\"message\":\"\",\"mode\":\"general\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Chat_Docs_Mode_Should_Return_400_When_Search_Is_Not_Configured()
    {
        await using var app = await BuildApiTestAppAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        using var response = await client.PostAsync(
            "/v1/chat",
            new StringContent("{\"message\":\"what is this doc about?\",\"mode\":\"docs\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Chat_General_Mode_Should_Return_200_With_Response_Envelope()
    {
        await using var app = await BuildApiTestAppAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        using var response = await client.PostAsync(
            "/v1/chat",
            new StringContent("{\"message\":\"hello\",\"mode\":\"general\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("answer").GetString().ShouldBe("stubbed-answer");
        payload.RootElement.TryGetProperty("citations", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Chat_Docs_Mode_Should_Return_200_With_Citations_When_Local_Search_Returns_Context()
    {
        await using var app = await BuildApiTestAppWithDocsModeConfiguredAsync(localRagFastResponse: false);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        using var response = await client.PostAsync(
            "/v1/chat",
            new StringContent("{\"message\":\"summarize the document\",\"mode\":\"docs\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("answer").GetString().ShouldBe("stubbed-answer");

        var citations = payload.RootElement.GetProperty("citations");
        citations.ValueKind.ShouldBe(JsonValueKind.Array);
        citations.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Chat_Docs_Mode_Should_Return_Fast_Local_Rag_Response_When_Configured()
    {
        await using var app = await BuildApiTestAppWithDocsModeConfiguredAsync(localRagFastResponse: true);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        using var response = await client.PostAsync(
            "/v1/chat",
            new StringContent("{\"message\":\"summarize the document\",\"mode\":\"docs\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var answer = payload.RootElement.GetProperty("answer").GetString();
        answer.ShouldNotBeNull();
        answer.ShouldContain("This is indexed context from the document.");

        var citations = payload.RootElement.GetProperty("citations");
        citations.ValueKind.ShouldBe(JsonValueKind.Array);
        citations.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Upload_Should_Return_400_When_Upload_Pipeline_Not_Configured()
    {
        await using var app = await BuildApiTestAppAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("dummy"), "file", "test.txt");

        using var response = await client.PostAsync("/v1/uploads", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_Should_Return_400_When_ContentType_Is_Not_Multipart()
    {
        await using var app = await BuildApiTestAppWithUploadConfiguredAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        using var response = await client.PostAsync(
            "/v1/uploads",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SignedUpload_Should_Return_400_When_Upload_Pipeline_Not_Configured()
    {
        await using var app = await BuildApiTestAppAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        using var response = await client.PostAsync(
            "/v1/uploads/signed-url",
            new StringContent("{\"fileName\":\"test.txt\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SignedUpload_Should_Return_400_For_Unsupported_File_Extension()
    {
        await using var app = await BuildApiTestAppWithUploadConfiguredAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        using var response = await client.PostAsync(
            "/v1/uploads/signed-url",
            new StringContent("{\"fileName\":\"malware.exe\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ingest_Should_Return_401_When_Unauthenticated()
    {
        await using var app = await BuildApiTestAppAsync();
        var client = app.GetTestClient();

        using var response = await client.PostAsync(
            "/v1/ingest",
            new StringContent($"{{\"documentId\":\"{Guid.NewGuid()}\"}}", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngestionStatus_Should_Return_401_When_Unauthenticated()
    {
        await using var app = await BuildApiTestAppAsync();
        var client = app.GetTestClient();

        using var response = await client.GetAsync($"/v1/uploads/{Guid.NewGuid()}/status");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ingest_And_Status_Should_Return_Accepted_And_Current_State_When_Authenticated()
    {
        await using var app = await BuildApiTestAppAsync();
        var store = (InMemoryDocumentIngestionStore)app.Services.GetRequiredService<IDocumentIngestionStore>();
        var documentId = Guid.NewGuid();
        await store.CreateOrUpdateJobAsync(documentId, "sample.txt", $"{documentId:N}/sample.txt", "PendingUpload", 5);

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("x-test-auth", "true");

        using var ingestResponse = await client.PostAsync(
            "/v1/ingest",
            new StringContent($"{{\"documentId\":\"{documentId}\"}}", Encoding.UTF8, "application/json"));

        ingestResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var statusResponse = await client.GetAsync($"/v1/uploads/{documentId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("documentId").GetGuid().ShouldBe(documentId);
        payload.RootElement.GetProperty("status").GetString().ShouldBe("Queued");
    }

    private static async Task<WebApplication> BuildApiTestAppAsync()
    {
        return await BuildApiTestAppInternalAsync(uploadConfigured: false);
    }

    private static async Task<WebApplication> BuildApiTestAppWithUploadConfiguredAsync()
    {
        return await BuildApiTestAppInternalAsync(uploadConfigured: true);
    }

    private static async Task<WebApplication> BuildApiTestAppWithDocsModeConfiguredAsync(bool localRagFastResponse = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory());
        builder.Services.AddSingleton<IDocumentIngestionStore, InMemoryDocumentIngestionStore>();
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
            options.AddPolicy(EntraAuthSetup.ApiScopePolicyName, policy =>
                policy.RequireClaim("scp", "access_as_user"));
        });

        builder.Services.AddSingleton<IAiService>(new StubAiService());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        var chatOptions = new ChatOptions(
            AiMode: "local",
            AzureOpenAi: null,
            OpenAiAuthMode: "api-key",
            EmbeddingModelId: "nomic-embed-text",
            EmbeddingDimensions: 768,
            LocalLlmEmbedBaseUrl: "http://local-llm",
            QdrantUrl: "http://local-qdrant",
            QdrantCollection: "documents",
            SearchEndpoint: string.Empty,
            SearchIndexName: string.Empty,
            SearchApiKey: null,
            ManagedIdentityClientId: null,
            UseManagedIdentity: false,
            LocalRagFastResponse: localRagFastResponse);

        var ingestionOptions = new DocumentIngestionOptions(
            SqlConnectionString: "Server=tcp:dummy,1433;Initial Catalog=dummy;User Id=dummy;Password=dummy;",
            WorkerDaprBaseUrl: "http://localhost:3500/v1.0/invoke/worker/method",
            StorageAccountName: string.Empty,
            StorageConnectionString: string.Empty,
            StorageContainerName: string.Empty,
            StorageAuthMode: "managed-identity",
            StoragePublicBlobEndpoint: string.Empty,
            ManagedIdentityClientId: null,
            UploadUrlLifetime: TimeSpan.FromMinutes(15));

        app.MapChatEndpoint(chatOptions);
        app.MapDocumentIngestionEndpoints(ingestionOptions);

        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> BuildApiTestAppInternalAsync(bool uploadConfigured)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<IDocumentIngestionStore, InMemoryDocumentIngestionStore>();
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
            options.AddPolicy(EntraAuthSetup.ApiScopePolicyName, policy =>
                policy.RequireClaim("scp", "access_as_user"));
        });

        builder.Services.AddSingleton<IAiService>(new StubAiService());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        var chatOptions = new ChatOptions(
            AiMode: "azure",
            AzureOpenAi: null,
            OpenAiAuthMode: "api-key",
            EmbeddingModelId: "text-embedding-3-small",
            EmbeddingDimensions: 1536,
            LocalLlmEmbedBaseUrl: "http://localhost:8080",
            QdrantUrl: "",
            QdrantCollection: "",
            SearchEndpoint: "",
            SearchIndexName: "",
            SearchApiKey: null,
            ManagedIdentityClientId: null,
            UseManagedIdentity: false);

        var ingestionOptions = uploadConfigured
            ? new DocumentIngestionOptions(
                SqlConnectionString: "Server=tcp:dummy,1433;Initial Catalog=dummy;User Id=dummy;Password=dummy;",
                WorkerDaprBaseUrl: "http://localhost:3500/v1.0/invoke/worker/method",
                StorageAccountName: "dummyaccount",
                StorageConnectionString: string.Empty,
                StorageContainerName: "documents",
                StorageAuthMode: "managed-identity",
                StoragePublicBlobEndpoint: string.Empty,
                ManagedIdentityClientId: null,
                UploadUrlLifetime: TimeSpan.FromMinutes(15))
            : new DocumentIngestionOptions(
                SqlConnectionString: "Server=tcp:dummy,1433;Initial Catalog=dummy;User Id=dummy;Password=dummy;",
                WorkerDaprBaseUrl: "http://localhost:3500/v1.0/invoke/worker/method",
                StorageAccountName: string.Empty,
                StorageConnectionString: string.Empty,
                StorageContainerName: string.Empty,
                StorageAuthMode: "managed-identity",
                StoragePublicBlobEndpoint: string.Empty,
                ManagedIdentityClientId: null,
                UploadUrlLifetime: TimeSpan.FromMinutes(15));

        app.MapChatEndpoint(chatOptions);
        app.MapDocumentIngestionEndpoints(ingestionOptions);

        await app.StartAsync();
        return app;
    }

    private sealed class StubAiService : IAiService
    {
        public Task<string> InvokePromptAsync(string prompt)
        {
            return Task.FromResult("stubbed-answer");
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHttpMessageHandler(), disposeHandler: true);
        }
    }

    private sealed class InMemoryDocumentIngestionStore : IDocumentIngestionStore
    {
        private readonly Dictionary<Guid, DocumentIngestionStatus> _jobs = new();
        private readonly object _sync = new();

        public Task CreateOrUpdateJobAsync(Guid documentId, string fileName, string blobName, string status, int progressPercent)
        {
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                if (_jobs.TryGetValue(documentId, out var existing))
                {
                    _jobs[documentId] = existing with
                    {
                        FileName = fileName,
                        BlobName = blobName,
                        Status = status,
                        ProgressPercent = progressPercent,
                        ErrorMessage = null,
                        UpdatedAtUtc = now
                    };
                }
                else
                {
                    _jobs[documentId] = new DocumentIngestionStatus(
                        documentId,
                        fileName,
                        blobName,
                        status,
                        progressPercent,
                        null,
                        null,
                        now,
                        now,
                        null);
                }
            }

            return Task.CompletedTask;
        }

        public Task UpdateJobStatusAsync(Guid documentId, string status, int progressPercent, string? errorMessage, int? totalChunks = null, bool isReady = false)
        {
            lock (_sync)
            {
                if (_jobs.TryGetValue(documentId, out var existing))
                {
                    _jobs[documentId] = existing with
                    {
                        Status = status,
                        ProgressPercent = progressPercent,
                        ErrorMessage = errorMessage,
                        TotalChunks = totalChunks ?? existing.TotalChunks,
                        ReadyAtUtc = isReady ? DateTime.UtcNow : existing.ReadyAtUtc,
                        UpdatedAtUtc = DateTime.UtcNow
                    };
                }
            }

            return Task.CompletedTask;
        }

        public Task<DocumentIngestionStatus?> GetJobAsync(Guid documentId)
        {
            lock (_sync)
            {
                return Task.FromResult(_jobs.TryGetValue(documentId, out var value)
                    ? value
                    : null);
            }
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.EndsWith("/v1/embeddings", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[{\"embedding\":[0.12,0.34,0.56]}]}", Encoding.UTF8, "application/json")
                });
            }

            if (path.Contains("/collections/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("/points/search", StringComparison.OrdinalIgnoreCase))
            {
                const string qdrantResponse = """
{
  "result": [
    {
      "id": "chunk-1",
      "payload": {
        "documentId": "doc-1",
        "chunkId": "chunk-1",
        "fileName": "sample.txt",
        "content": "This is indexed context from the document."
      }
    }
  ]
}
""";

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(qdrantResponse, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
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

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
                new Claim(ClaimTypes.Name, "integration-user"),
                new Claim("scp", "access_as_user")
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
