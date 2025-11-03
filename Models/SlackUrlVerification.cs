using System.Text.Json.Serialization;

namespace skybot.Models;

internal record SlackUrlVerification([property: JsonPropertyName("token")] string Token, [property: JsonPropertyName("challenge")] string Challenge, [property: JsonPropertyName("type")] string Type);