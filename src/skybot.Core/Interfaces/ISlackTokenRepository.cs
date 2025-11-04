using skybot.Core.Models;

namespace skybot.Core.Interfaces;

public interface ISlackTokenRepository
{
    Task StoreTokenAsync(string accessToken, string teamId, string teamName);
    Task<SlackToken?> GetTokenAsync(string teamId);
    Task DeleteTokenAsync(string teamId);
    Task<List<SlackToken>> GetAllTokensAsync();
    Task<Dictionary<string, SlackToken>> GetTokensByTeamIdsAsync(IEnumerable<string> teamIds);
}

