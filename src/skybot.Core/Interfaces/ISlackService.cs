using skybot.Core.Models;
using skybot.Core.Models.Slack;

namespace skybot.Core.Interfaces;

public interface ISlackService
{
    Task<bool> SendMessageAsync(string token, string channel, string text, string? threadTs = null);
    Task<CreateChannelResult> CreateChannelAsync(string teamId, string name, HttpClient? httpClient = null);
    Task<ListMembersResult> ListChannelMembersAsync(string teamId, string channelId, HttpClient? httpClient = null, int maxMembers = 10);
    Task<string?> GetTeamIdFromTokenAsync(string token, HttpClient? httpClient = null);
    Task<bool> SendBlocksAsync(string token, string channel, object[] blocks, string? threadTs = null);
    Task<SendMessageResult> SendMessageAsync(string teamId, SendMessageRequest request);
    Task<DeleteMessageResult> DeleteMessageAsync(string teamId, string channel, string messageTs);
}

