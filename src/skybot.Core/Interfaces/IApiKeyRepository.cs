using skybot.Core.Models.Auth;

namespace skybot.Core.Interfaces;

public interface IApiKeyRepository
{
    Task<ApiKey?> GetByKeyAsync(string apiKey);
    Task<List<ApiKey>> GetByTeamIdAsync(string teamId);
    Task<string> CreateAsync(string teamId, string name, List<string>? allowedEndpoints = null, DateTime? expiresAt = null);
    Task<bool> RevokeAsync(string apiKey);
    Task UpdateLastUsedAsync(string apiKey);
}

