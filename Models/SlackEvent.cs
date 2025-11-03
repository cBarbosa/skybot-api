using System.Text.Json.Serialization;

internal record SlackEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("user")] string? User,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("bot_id")] string? BotId,
    [property: JsonPropertyName("subtype")] string? Subtype,
    [property: JsonPropertyName("ts")] string Ts); // opcional: thread_ts