using System.Text.Json.Serialization;

namespace skybot.Core.Models;

public record SlackUrlVerification(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("challenge")] string Challenge,
    [property: JsonPropertyName("type")] string Type);

