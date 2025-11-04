namespace skybot.Core.Models;

public record CreateChannelResult(
    bool Success,
    string Message,
    string? ChannelId = null,
    string? ChannelName = null
);

