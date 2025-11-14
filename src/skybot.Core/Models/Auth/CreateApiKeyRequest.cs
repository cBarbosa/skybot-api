namespace skybot.Core.Models.Auth;

public record CreateApiKeyRequest(
    string TeamId,
    string Name,
    List<string>? AllowedEndpoints = null,
    DateTime? ExpiresAt = null
);

