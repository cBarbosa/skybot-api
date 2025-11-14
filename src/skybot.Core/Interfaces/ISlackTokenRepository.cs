using skybot.Core.Models;

namespace skybot.Core.Interfaces;

public interface ISlackTokenRepository
{
    Task StoreTokenAsync(string accessToken, string? refreshToken, string teamId, string teamName);
    Task UpdateTokenAsync(string teamId, string accessToken, string? refreshToken);
    Task<SlackToken?> GetTokenAsync(string teamId);
    Task DeleteTokenAsync(string teamId);
    Task<List<SlackToken>> GetAllTokensAsync();
    Task<Dictionary<string, SlackToken>> GetTokensByTeamIdsAsync(IEnumerable<string> teamIds);
}

