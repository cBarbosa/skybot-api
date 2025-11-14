using System.Text.Json.Serialization;

namespace skybot.Core.Models.Slack;

public record SendMessageRequest(
    [property: JsonPropertyName("destinationType")] DestinationType DestinationType,
    [property: JsonPropertyName("destinationId")] string DestinationId,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("blocks")] List<object>? Blocks = null,
    [property: JsonPropertyName("threadTs")] string? ThreadTs = null
);

