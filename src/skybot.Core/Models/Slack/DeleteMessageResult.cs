namespace skybot.Core.Models.Slack;

public record DeleteMessageResult(
    bool Success,
    string? Error = null
);

public record DeleteMessageRequest(
    string Channel,
    string MessageTs
);

