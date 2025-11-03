namespace skybot.Models;

public record SlackUserInfo(
    string UserId,
    string DisplayName,
    string? RealName = null,
    string? Email = null
);