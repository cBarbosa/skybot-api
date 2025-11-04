using skybot.Core.Interfaces;

namespace skybot.Core.Services.Infrastructure;

public class CacheService : ICacheService
{
    // Cache para evitar processar eventos duplicados do Slack
    // Usa ConcurrentDictionary para thread-safety
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _processedEvents = new();
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);

    // Cache para rastrear tentativas de comandos não encontrados
    // Chave: TeamId_UserId_Channel_ThreadTs, Valor: número de tentativas
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _commandAttempts = new();

    // Cache para armazenar mensagens pendentes de confirmação de agente virtual
    // Chave: TeamId_UserId_Channel_ThreadTs, Valor: objeto com mensagem e timestamp
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Message, string ThreadTs, DateTime Timestamp)> _pendingAIMessages = new();

    // Cache para rastrear threads que estão em modo agente virtual
    // Chave: TeamId_UserId_Channel_ThreadTs, Valor: timestamp da ativação
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _aiModeThreads = new();

    public CacheService()
    {
        // Inicia limpeza periódica de cache
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(_cleanupInterval);
                ClearExpiredEntries();
            }
        });
    }

    public bool IsEventProcessed(string eventId)
    {
        return _processedEvents.ContainsKey(eventId);
    }

    public void MarkEventAsProcessed(string eventId)
    {
        _processedEvents.TryAdd(eventId, DateTime.UtcNow);
    }

    public int GetCommandAttempts(string key)
    {
        return _commandAttempts.TryGetValue(key, out var attempts) ? attempts : 0;
    }

    public void IncrementCommandAttempts(string key)
    {
        _commandAttempts.AddOrUpdate(key, 1, (k, oldValue) => oldValue + 1);
    }

    public void ResetCommandAttempts(string key)
    {
        _commandAttempts.TryRemove(key, out _);
    }

    public void SetPendingAIMessage(string key, string message, string threadTs)
    {
        _pendingAIMessages.AddOrUpdate(key, 
            (message, threadTs, DateTime.UtcNow),
            (k, oldValue) => (message, threadTs, DateTime.UtcNow));
    }

    public (string Message, string ThreadTs, DateTime Timestamp)? GetPendingAIMessage(string key)
    {
        return _pendingAIMessages.TryGetValue(key, out var pending) ? pending : null;
    }

    public void RemovePendingAIMessage(string key)
    {
        _pendingAIMessages.TryRemove(key, out _);
    }

    public bool IsThreadInAIMode(string key)
    {
        return _aiModeThreads.ContainsKey(key);
    }

    public void SetThreadAIMode(string key)
    {
        _aiModeThreads.AddOrUpdate(key, DateTime.UtcNow, (k, oldValue) => DateTime.UtcNow);
    }

    public void RemoveThreadAIMode(string key)
    {
        _aiModeThreads.TryRemove(key, out _);
    }

    public void ClearExpiredEntries()
    {
        var cutoff = DateTime.UtcNow.Subtract(_cleanupInterval);
        
        // Limpa eventos antigos
        var keysToRemove = _processedEvents
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in keysToRemove)
        {
            _processedEvents.TryRemove(key, out _);
        }
        
        // Limpa também tentativas e mensagens pendentes antigas
        var oldAttempts = _commandAttempts
            .Where(kvp => _pendingAIMessages.TryGetValue(kvp.Key, out var pending) && pending.Timestamp < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in oldAttempts)
        {
            _commandAttempts.TryRemove(key, out _);
            _pendingAIMessages.TryRemove(key, out _);
        }
        
        // Limpa threads em modo agente virtual antigas (mais de 24 horas)
        var oldAiModeThreads = _aiModeThreads
            .Where(kvp => kvp.Value < cutoff.AddHours(-23))
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in oldAiModeThreads)
        {
            _aiModeThreads.TryRemove(key, out _);
        }
        
        if (keysToRemove.Count > 0 || oldAttempts.Count > 0 || oldAiModeThreads.Count > 0)
        {
            Console.WriteLine($"[INFO] Limpeza de cache: removidos {keysToRemove.Count} eventos, {oldAttempts.Count} tentativas e {oldAiModeThreads.Count} threads de agente virtual antigas");
        }
    }
}

