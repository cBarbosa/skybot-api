namespace skybot.Models;

internal record CreateChannelResult(
    bool Success,
    string Message,
    string? ChannelId = null,
    string? ChannelName = null
);