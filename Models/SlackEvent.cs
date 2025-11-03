using System.Text.Json.Serialization;

internal record SlackEvent([property: JsonPropertyName("type")] string Type, [property: JsonPropertyName("user")] string User, [property: JsonPropertyName("text")] string Text, [property: JsonPropertyName("channel")] string Channel);