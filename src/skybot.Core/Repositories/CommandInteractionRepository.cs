using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using skybot.Core.Interfaces;
using skybot.Core.Models.Commands;

namespace skybot.Core.Repositories;

public class CommandInteractionRepository : ICommandInteractionRepository
{
    private readonly string _connectionString;

    public CommandInteractionRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySqlConnection") 
            ?? throw new InvalidOperationException("Connection string não configurada");
    }

    private IDbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<int> CreateAsync(CreateCommandInteractionRequest request)
    {
        const string query = @"
            INSERT INTO CommandInteractions (
                TeamId, UserId, InteractionType, Command, ActionId, Arguments,
                Channel, ThreadTs, MessageTs, SourceIp, UserAgent, Success, ErrorMessage
            ) VALUES (
                @TeamId, @UserId, @InteractionType, @Command, @ActionId, @Arguments,
                @Channel, @ThreadTs, @MessageTs, @SourceIp, @UserAgent, @Success, @ErrorMessage
            );
            SELECT LAST_INSERT_ID();";

        using var connection = CreateConnection();
        return await connection.ExecuteScalarAsync<int>(query, new
        {
            request.TeamId,
            request.UserId,
            InteractionType = request.InteractionType.ToString(),
            request.Command,
            request.ActionId,
            request.Arguments,
            request.Channel,
            request.ThreadTs,
            request.MessageTs,
            request.SourceIp,
            request.UserAgent,
            request.Success,
            request.ErrorMessage
        });
    }

    public async Task<List<CommandInteraction>> GetByTeamAsync(string teamId, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT * FROM CommandInteractions 
            WHERE TeamId = @TeamId 
            AND ExecutedAt BETWEEN @StartDate AND @EndDate
            ORDER BY ExecutedAt DESC";

        using var connection = CreateConnection();
        var interactions = await connection.QueryAsync<CommandInteraction>(query, new 
        { 
            TeamId = teamId, 
            StartDate = startDate, 
            EndDate = endDate 
        });
        
        return interactions.ToList();
    }

    public async Task<List<CommandInteraction>> GetByUserAsync(string teamId, string userId, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT * FROM CommandInteractions 
            WHERE TeamId = @TeamId 
            AND UserId = @UserId
            AND ExecutedAt BETWEEN @StartDate AND @EndDate
            ORDER BY ExecutedAt DESC";

        using var connection = CreateConnection();
        var interactions = await connection.QueryAsync<CommandInteraction>(query, new 
        { 
            TeamId = teamId,
            UserId = userId,
            StartDate = startDate, 
            EndDate = endDate 
        });
        
        return interactions.ToList();
    }

    public async Task<Dictionary<string, int>> GetCommandStatsAsync(string teamId, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT 
                COALESCE(Command, ActionId) as Name,
                COUNT(*) as Count
            FROM CommandInteractions
            WHERE TeamId = @TeamId
            AND ExecutedAt BETWEEN @StartDate AND @EndDate
            AND Success = TRUE
            GROUP BY COALESCE(Command, ActionId)
            ORDER BY Count DESC";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(string Name, int Count)>(
            query, 
            new { TeamId = teamId, StartDate = startDate, EndDate = endDate }
        );

        return results.ToDictionary(r => r.Name ?? "unknown", r => r.Count);
    }

    public async Task<Dictionary<string, int>> GetInteractionTypeStatsAsync(string teamId, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT 
                InteractionType,
                COUNT(*) as Count
            FROM CommandInteractions
            WHERE TeamId = @TeamId
            AND ExecutedAt BETWEEN @StartDate AND @EndDate
            AND Success = TRUE
            GROUP BY InteractionType";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(string InteractionType, int Count)>(
            query, 
            new { TeamId = teamId, StartDate = startDate, EndDate = endDate }
        );

        return results.ToDictionary(r => r.InteractionType, r => r.Count);
    }

    public async Task<Dictionary<string, int>> GetDailyStatsAsync(string teamId, DateTime date)
    {
        const string query = @"
            SELECT 
                InteractionType,
                COUNT(*) as Count
            FROM CommandInteractions
            WHERE TeamId = @TeamId
            AND DATE(ExecutedAt) = DATE(@Date)
            AND Success = TRUE
            GROUP BY InteractionType";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(string InteractionType, int Count)>(
            query, 
            new { TeamId = teamId, Date = date }
        );

        return results.ToDictionary(r => r.InteractionType, r => r.Count);
    }

    public async Task<Dictionary<string, int>> GetWeeklyStatsAsync(string teamId, DateTime startDate)
    {
        var endDate = startDate.AddDays(7);
        
        const string query = @"
            SELECT 
                DATE(ExecutedAt) as Date,
                COUNT(*) as Count
            FROM CommandInteractions
            WHERE TeamId = @TeamId
            AND ExecutedAt BETWEEN @StartDate AND @EndDate
            AND Success = TRUE
            GROUP BY DATE(ExecutedAt)
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
                DAY(ExecutedAt) as Day,
                COUNT(*) as Count
            FROM CommandInteractions
            WHERE TeamId = @TeamId
            AND YEAR(ExecutedAt) = @Year
            AND MONTH(ExecutedAt) = @Month
            AND Success = TRUE
            GROUP BY DAY(ExecutedAt)
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
            FROM CommandInteractions
            WHERE TeamId = @TeamId
            AND ExecutedAt BETWEEN @StartDate AND @EndDate
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

    public async Task<List<(string ActionId, int Count)>> GetMostUsedButtonsAsync(string teamId, DateTime startDate, DateTime endDate, int limit = 10)
    {
        const string query = @"
            SELECT 
                ActionId,
                COUNT(*) as Count
            FROM CommandInteractions
            WHERE TeamId = @TeamId
            AND ExecutedAt BETWEEN @StartDate AND @EndDate
            AND InteractionType IN ('BUTTON', 'MODAL')
            AND ActionId IS NOT NULL
            AND Success = TRUE
            GROUP BY ActionId
            ORDER BY Count DESC
            LIMIT @Limit";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(string ActionId, int Count)>(
            query, 
            new { TeamId = teamId, StartDate = startDate, EndDate = endDate, Limit = limit }
        );

        return results.ToList();
    }
}

