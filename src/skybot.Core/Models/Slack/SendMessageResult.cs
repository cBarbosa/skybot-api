namespace skybot.Core.Models.Slack;

public record SendMessageResult(
    bool Success,
    string? MessageTs = null,
    string? Error = null
);

