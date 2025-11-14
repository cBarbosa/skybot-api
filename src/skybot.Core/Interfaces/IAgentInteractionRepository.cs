using skybot.Core.Models.Commands;

namespace skybot.Core.Interfaces;

public interface IAgentInteractionRepository
{
    /// <summary>
    /// Cria um novo registro de interação com o agente
    /// </summary>
    Task<int> CreateAsync(CreateAgentInteractionRequest request);
    
    /// <summary>
    /// Obtém todas as interações de um team em um período
    /// </summary>
    Task<List<AgentInteraction>> GetByTeamAsync(string teamId, DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// Obtém estatísticas de uso por provider
    /// </summary>
    Task<Dictionary<string, int>> GetProviderStatsAsync(string teamId, DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// Obtém estatísticas diárias
    /// </summary>
    Task<Dictionary<string, int>> GetDailyStatsAsync(string teamId, DateTime date);
    
    /// <summary>
    /// Obtém estatísticas semanais
    /// </summary>
    Task<Dictionary<string, int>> GetWeeklyStatsAsync(string teamId, DateTime startDate);
    
    /// <summary>
    /// Obtém estatísticas mensais
    /// </summary>
    Task<Dictionary<string, int>> GetMonthlyStatsAsync(string teamId, int year, int month);
    
    /// <summary>
    /// Obtém top usuários mais ativos no agente
    /// </summary>
    Task<List<(string UserId, int Count)>> GetTopUsersAsync(string teamId, DateTime startDate, DateTime endDate, int limit = 10);
    
    /// <summary>
    /// Obtém threads com mais interações
    /// </summary>
    Task<List<(string ThreadKey, int Count)>> GetTopThreadsAsync(string teamId, DateTime startDate, DateTime endDate, int limit = 10);
    
    /// <summary>
    /// Obtém estatísticas de custos
    /// </summary>
    Task<(int TotalInteractions, decimal? TotalCost)> GetCostStatsAsync(string teamId, DateTime startDate, DateTime endDate);
}

