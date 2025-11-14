using skybot.Core.Models;

namespace skybot.Core.Interfaces;

public interface ITokenRefreshHistoryRepository
{
    Task AddHistoryAsync(string teamId, string refreshToken, bool success, string? errorMessage, string? oldAccessToken, string? newAccessToken);
    Task<List<TokenRefreshHistory>> GetHistoryByTeamIdAsync(string teamId, int limit = 50);
    Task<List<TokenRefreshHistory>> GetRecentFailuresAsync(int hours = 24);
}

