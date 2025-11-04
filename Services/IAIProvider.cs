namespace skybot.Services;

internal interface IAIProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<string?> GetResponseAsync(string userMessage, string systemPrompt);
    Task<string?> GetResponseAsync(string userMessage, string systemPrompt, List<(string Role, string Content)>? conversationHistory);
}


