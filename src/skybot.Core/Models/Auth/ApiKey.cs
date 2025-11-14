namespace skybot.Core.Models.Auth;

public record ApiKey(
    int Id,
    string TeamId,
    string Key,
    string Name,
    bool IsActive,
    List<string>? AllowedEndpoints,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    DateTime? ExpiresAt
);

