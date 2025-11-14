using skybot.Core.Models;

namespace skybot.Core.Interfaces;

public interface ICommandService
{
    Task ExecuteCommandAsync(string command, string args, SlackEvent slackEvent, string accessToken, string teamId);
    Task ShowHelpAsync(SlackEvent slackEvent, string accessToken);
    Task ShowTimeAsync(SlackEvent slackEvent, string accessToken);
    Task CreateChannelAsync(string teamId, string name, SlackEvent slackEvent, string accessToken);
    Task ListMembersAsync(string teamId, string channelId, SlackEvent slackEvent, string accessToken, int maxMembers = 10);
    Task ShowRemindersMenuAsync(SlackEvent slackEvent, string accessToken);
}

