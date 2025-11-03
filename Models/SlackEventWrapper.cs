using System.Text.Json.Serialization;

namespace skybot.Models;

internal record SlackEventWrapper(
    [property: JsonPropertyName("team_id")] string TeamId,
    [property: JsonPropertyName("event")] SlackEvent Event); // ← referência