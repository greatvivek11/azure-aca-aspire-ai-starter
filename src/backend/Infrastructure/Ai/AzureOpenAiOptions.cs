namespace AcaAspireAiTemplate.Backend.Infrastructure.Ai;

public class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAI";

    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}
