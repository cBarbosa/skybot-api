namespace skybot.Core.Models;

public record TokenRefreshHistory(
    int Id,
    string TeamId,
    string RefreshToken,
    bool Success,
    string? ErrorMessage,
    DateTime RefreshedAt,
    string? OldAccessToken,
    string? NewAccessToken);

