using skybot.Core.Models.Slack;

namespace skybot.Core.Interfaces;

public interface IMessageLogRepository
{
    Task<int> CreateLogAsync(CreateMessageLogRequest request);
    Task<MessageLog?> GetByMessageTsAsync(string messageTs);
    Task<bool> MarkAsDeletedAsync(string messageTs);
    Task<bool> MarkAsFailedAsync(string messageTs, string errorMessage);
    
    // Métodos para relatórios
    Task<List<MessageLog>> GetLogsByTeamAsync(string teamId, DateTime startDate, DateTime endDate);
    Task<Dictionary<string, int>> GetDailyStatsAsync(string teamId, DateTime date);
    Task<Dictionary<string, int>> GetWeeklyStatsAsync(string teamId, DateTime startDate);
    Task<Dictionary<string, int>> GetMonthlyStatsAsync(string teamId, int year, int month);
    
    // Método para auditoria
    Task<List<MessageLog>> GetAuditLogsAsync(string teamId, string? ip, string? apiKeyName, DateTime startDate, DateTime endDate);
}

