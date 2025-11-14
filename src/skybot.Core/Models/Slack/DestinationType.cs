using System.Text.Json.Serialization;

namespace skybot.Core.Models.Slack;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DestinationType
{
    CHANNEL,
    USER,
    GROUP
}

