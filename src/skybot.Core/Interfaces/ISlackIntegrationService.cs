using skybot.Core.Models;

namespace skybot.Core.Interfaces;

public interface ISlackIntegrationService
{
    Task ProcessSlackEventAsync(SlackEventWrapper eventWrapper);
    Task ProcessInteractiveEventAsync(string payload);
    Task<string?> GetAIResponseForThreadAsync(string userMessage, string threadKey, string? context = null);
}

