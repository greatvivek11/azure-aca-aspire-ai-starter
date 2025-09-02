using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

[assembly: UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Required for Semantic Kernel dynamic functionality")]
[assembly: UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Required for Semantic Kernel dynamic functionality")]

// Add JsonSerializable attributes for AOT compilation
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(AIHub.Backend.Features.Health.HealthResponse))]
[JsonSerializable(typeof(AIHub.Backend.Features.AiPing.AiPingResponse))]
internal partial class AotJsonSerializerContext : JsonSerializerContext
{
}