namespace AcaAspireAiTemplate.Backend.Features.Chat;

internal interface IChatEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}
