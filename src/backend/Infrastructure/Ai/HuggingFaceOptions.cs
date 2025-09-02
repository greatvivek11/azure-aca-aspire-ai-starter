namespace AIHub.Backend.Infrastructure.Ai;

public class HuggingFaceOptions
{
    public const string SectionName = "HuggingFace";
    
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}