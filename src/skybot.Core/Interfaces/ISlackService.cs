using skybot.Core.Models;

namespace skybot.Core.Interfaces;

public interface ISlackService
{
    Task<bool> SendMessageAsync(string token, string channel, string text, string? threadTs = null);
    Task<CreateChannelResult> CreateChannelAsync(string teamId, string name, HttpClient? httpClient = null);
    Task<ListMembersResult> ListChannelMembersAsync(string teamId, string channelId, HttpClient? httpClient = null, int maxMembers = 10);
    Task<string?> GetTeamIdFromTokenAsync(string token, HttpClient? httpClient = null);
    Task<bool> SendBlocksAsync(string token, string channel, object[] blocks, string? threadTs = null);
}

