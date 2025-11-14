namespace skybot.Core.Models;

public record ListMembersResult(
    bool Success,
    string Message,
    IReadOnlyList<SlackUserInfo>? Members = null
);

