namespace skybot.Core.Interfaces;

public interface ICacheService
{
    bool IsEventProcessed(string eventId);
    void MarkEventAsProcessed(string eventId);
    int GetCommandAttempts(string key);
    void IncrementCommandAttempts(string key);
    void ResetCommandAttempts(string key);
    void SetPendingAIMessage(string key, string message, string threadTs);
    (string Message, string ThreadTs, DateTime Timestamp)? GetPendingAIMessage(string key);
    void RemovePendingAIMessage(string key);
    bool IsThreadInAIMode(string key);
    void SetThreadAIMode(string key);
    void RemoveThreadAIMode(string key);
    void ClearExpiredEntries();
}

