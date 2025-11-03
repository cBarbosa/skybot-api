// Modelo para a resposta do Slack OAuth

using System.Text.Json.Serialization;

namespace skybot.Models;

internal record SlackOAuthResponse(
    [property: JsonPropertyName("ok")]           bool Ok,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("team")]         SlackTeam? Team,
    [property: JsonPropertyName("error")]        string? Error = null);

internal record SlackTeam(
    [property: JsonPropertyName("id")]   string Id,
    [property: JsonPropertyName("name")] string Name);