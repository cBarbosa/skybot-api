using System.Text.Json.Serialization;

namespace skybot.Core.Models;

public record SlackOAuthResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("team")] SlackTeam? Team,
    [property: JsonPropertyName("error")] string? Error = null);

public record SlackTeam(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);

