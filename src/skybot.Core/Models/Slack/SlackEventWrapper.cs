using System.Text.Json.Serialization;

namespace skybot.Core.Models;

public record SlackEventWrapper(
    [property: JsonPropertyName("team_id")] string TeamId,
    [property: JsonPropertyName("event_id")] string? EventId,
    [property: JsonPropertyName("event")] SlackEvent Event);

