namespace skybot.Services;

internal interface IAIProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<string?> GetResponseAsync(string userMessage, string systemPrompt);
}


