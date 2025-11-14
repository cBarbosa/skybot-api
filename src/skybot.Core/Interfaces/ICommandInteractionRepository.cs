using skybot.Core.Models.Commands;

namespace skybot.Core.Interfaces;

public interface ICommandInteractionRepository
{
    /// <summary>
    /// Cria um novo registro de interação
    /// </summary>
    Task<int> CreateAsync(CreateCommandInteractionRequest request);
    
    /// <summary>
    /// Obtém todas as interações de um team em um período
    /// </summary>
    Task<List<CommandInteraction>> GetByTeamAsync(string teamId, DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// Obtém todas as interações de um usuário específico em um período
    /// </summary>
    Task<List<CommandInteraction>> GetByUserAsync(string teamId, string userId, DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// Obtém estatísticas de comandos agrupados por nome
    /// </summary>
    Task<Dictionary<string, int>> GetCommandStatsAsync(string teamId, DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// Obtém estatísticas agrupadas por tipo de interação
    /// </summary>
    Task<Dictionary<string, int>> GetInteractionTypeStatsAsync(string teamId, DateTime startDate, DateTime endDate);
    
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
    /// Obtém top usuários mais ativos
    /// </summary>
    Task<List<(string UserId, int Count)>> GetTopUsersAsync(string teamId, DateTime startDate, DateTime endDate, int limit = 10);
    
    /// <summary>
    /// Obtém botões mais clicados
    /// </summary>
    Task<List<(string ActionId, int Count)>> GetMostUsedButtonsAsync(string teamId, DateTime startDate, DateTime endDate, int limit = 10);
}

