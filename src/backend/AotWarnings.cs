using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

// Add JsonSerializable attributes for AOT compilation
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(AcaAspireAiTemplate.Backend.Features.Health.HealthResponse))]
[JsonSerializable(typeof(AcaAspireAiTemplate.Backend.Features.AiPing.AiPingResponse))]
internal partial class AotJsonSerializerContext : JsonSerializerContext
{
}