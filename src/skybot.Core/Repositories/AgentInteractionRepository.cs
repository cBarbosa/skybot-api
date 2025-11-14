using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using skybot.Core.Interfaces;
using skybot.Core.Models.Commands;

namespace skybot.Core.Repositories;

public class AgentInteractionRepository : IAgentInteractionRepository
{
    private readonly string _connectionString;

    public AgentInteractionRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySqlConnection") 
            ?? throw new InvalidOperationException("Connection string não configurada");
    }

    private IDbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<int> CreateAsync(CreateAgentInteractionRequest request)
    {
        const string query = @"
            INSERT INTO AgentInteractions (
                TeamId, UserId, ThreadKey, Channel, ThreadTs, MessageTs,
                UserMessageLength, AIProvider, AIModel, ResponseLength, ResponseTime,
                SourceIp, UserAgent, Success, ErrorMessage, TokensUsed, EstimatedCost
            ) VALUES (
                @TeamId, @UserId, @ThreadKey, @Channel, @ThreadTs, @MessageTs,
                @UserMessageLength, @AIProvider, @AIModel, @ResponseLength, @ResponseTime,
                @SourceIp, @UserAgent, @Success, @ErrorMessage, @TokensUsed, @EstimatedCost
            );
            SELECT LAST_INSERT_ID();";

        using var connection = CreateConnection();
        return await connection.ExecuteScalarAsync<int>(query, request);
    }

    public async Task<List<AgentInteraction>> GetByTeamAsync(string teamId, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT * FROM AgentInteractions 
            WHERE TeamId = @TeamId 
            AND InteractedAt BETWEEN @StartDate AND @EndDate
            ORDER BY InteractedAt DESC";

        using var connection = CreateConnection();
        var interactions = await connection.QueryAsync<AgentInteraction>(query, new 
        { 
            TeamId = teamId, 
            StartDate = startDate, 
            EndDate = endDate 
        });
        
        return interactions.ToList();
    }

    public async Task<Dictionary<string, int>> GetProviderStatsAsync(string teamId, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT 
                AIProvider,
                COUNT(*) as Count
            FROM AgentInteractions
            WHERE TeamId = @TeamId
            AND InteractedAt BETWEEN @StartDate AND @EndDate
            AND Success = TRUE
            GROUP BY AIProvider
            ORDER BY Count DESC";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(string AIProvider, int Count)>(
            query, 
            new { TeamId = teamId, StartDate = startDate, EndDate = endDate }
        );

        return results.ToDictionary(r => r.AIProvider, r => r.Count);
    }

    public async Task<Dictionary<string, int>> GetDailyStatsAsync(string teamId, DateTime date)
    {
        const string query = @"
            SELECT 
                AIProvider,
                COUNT(*) as Count
            FROM AgentInteractions
            WHERE TeamId = @TeamId
            AND DATE(InteractedAt) = DATE(@Date)
            AND Success = TRUE
            GROUP BY AIProvider";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(string AIProvider, int Count)>(
            query, 
            new { TeamId = teamId, Date = date }
        );

        return results.ToDictionary(r => r.AIProvider, r => r.Count);
    }

    public async Task<Dictionary<string, int>> GetWeeklyStatsAsync(string teamId, DateTime startDate)
    {
        var endDate = startDate.AddDays(7);
        
        const string query = @"
            SELECT 
                DATE(InteractedAt) as Date,
                COUNT(*) as Count
            FROM AgentInteractions
            WHERE TeamId = @TeamId
            AND InteractedAt BETWEEN @StartDate AND @EndDate
            AND Success = TRUE
            GROUP BY DATE(InteractedAt)
            ORDER BY Date";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(DateTime Date, int Count)>(
            query, 
            new { TeamId = teamId, StartDate = startDate, EndDate = endDate }
        );

        return results.ToDictionary(r => r.Date.ToString("yyyy-MM-dd"), r => r.Count);
    }

    public async Task<Dictionary<string, int>> GetMonthlyStatsAsync(string teamId, int year, int month)
    {
        const string query = @"
            SELECT 
                DAY(InteractedAt) as Day,
                COUNT(*) as Count
            FROM AgentInteractions
            WHERE TeamId = @TeamId
            AND YEAR(InteractedAt) = @Year
            AND MONTH(InteractedAt) = @Month
            AND Success = TRUE
            GROUP BY DAY(InteractedAt)
            ORDER BY Day";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(int Day, int Count)>(
            query, 
            new { TeamId = teamId, Year = year, Month = month }
        );

        return results.ToDictionary(r => r.Day.ToString(), r => r.Count);
    }

    public async Task<List<(string UserId, int Count)>> GetTopUsersAsync(string teamId, DateTime startDate, DateTime endDate, int limit = 10)
    {
        const string query = @"
            SELECT 
                UserId,
                COUNT(*) as Count
            FROM AgentInteractions
            WHERE TeamId = @TeamId
            AND InteractedAt BETWEEN @StartDate AND @EndDate
            AND Success = TRUE
            GROUP BY UserId
            ORDER BY Count DESC
            LIMIT @Limit";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(string UserId, int Count)>(
            query, 
            new { TeamId = teamId, StartDate = startDate, EndDate = endDate, Limit = limit }
        );

        return results.ToList();
    }

    public async Task<List<(string ThreadKey, int Count)>> GetTopThreadsAsync(string teamId, DateTime startDate, DateTime endDate, int limit = 10)
    {
        const string query = @"
            SELECT 
                ThreadKey,
                COUNT(*) as Count
            FROM AgentInteractions
            WHERE TeamId = @TeamId
            AND InteractedAt BETWEEN @StartDate AND @EndDate
            AND Success = TRUE
            GROUP BY ThreadKey
            ORDER BY Count DESC
            LIMIT @Limit";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(string ThreadKey, int Count)>(
            query, 
            new { TeamId = teamId, StartDate = startDate, EndDate = endDate, Limit = limit }
        );

        return results.ToList();
    }

    public async Task<(int TotalInteractions, decimal? TotalCost)> GetCostStatsAsync(string teamId, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT 
                COUNT(*) as TotalInteractions,
                SUM(EstimatedCost) as TotalCost
            FROM AgentInteractions
            WHERE TeamId = @TeamId
            AND InteractedAt BETWEEN @StartDate AND @EndDate
            AND Success = TRUE";

        using var connection = CreateConnection();
        var result = await connection.QueryFirstOrDefaultAsync<(int TotalInteractions, decimal? TotalCost)>(
            query, 
            new { TeamId = teamId, StartDate = startDate, EndDate = endDate }
        );

        return result;
    }
}

