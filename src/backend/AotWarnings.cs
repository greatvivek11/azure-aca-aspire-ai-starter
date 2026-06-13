using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

// Add JsonSerializable attributes for AOT compilation
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(AIHub.Backend.Features.Health.HealthResponse))]
[JsonSerializable(typeof(AIHub.Backend.Features.AiPing.AiPingResponse))]
internal partial class AotJsonSerializerContext : JsonSerializerContext
{
}