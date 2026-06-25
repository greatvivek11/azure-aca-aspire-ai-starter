using Microsoft.Extensions.DependencyInjection;

namespace AcaAspireAiTemplate.Backend.Features.Chat;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChatFeature(this IServiceCollection services, ChatOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<IManagedIdentityTokenProvider, ManagedIdentityTokenProvider>();
        services.AddSingleton<IChatEmbeddingService, ChatEmbeddingService>();
        services.AddSingleton<IChatSearchRetrievalService, ChatSearchRetrievalService>();
        services.AddSingleton<ChatRagOrchestrator>();
        return services;
    }
}
