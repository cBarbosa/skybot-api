namespace skybot.Core.Interfaces;

public interface IAIService
{
    bool IsConfigured { get; }
    Task<string?> GetAIResponseAsync(string userMessage, string? context = null);
    Task<string?> GetAIResponseAsync(string userMessage, string? context, string? threadKey);
    void ClearThreadProvider(string threadKey);
}

