using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using skybot.Core.Interfaces;
using skybot.Core.Models.Settings;

namespace skybot.Core.Repositories;

public class WorkspaceSettingsRepository : IWorkspaceSettingsRepository
{
    private readonly string _connectionString;

    public WorkspaceSettingsRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySqlConnection") 
            ?? throw new InvalidOperationException("Connection string não configurada");
    }

    private IDbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<WorkspaceSettings?> GetByTeamIdAsync(string teamId)
    {
        const string query = "SELECT * FROM WorkspaceSettings WHERE TeamId = @TeamId";
        
        using var connection = CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<WorkspaceSettings>(query, new { TeamId = teamId });
    }

    public async Task<List<WorkspaceSettings>> GetAllAsync()
    {
        const string query = "SELECT * FROM WorkspaceSettings";
        
        using var connection = CreateConnection();
        var settings = await connection.QueryAsync<WorkspaceSettings>(query);
        return settings.ToList();
    }

    public async Task CreateAsync(string teamId, string adminUserId)
    {
        const string query = @"
            INSERT INTO WorkspaceSettings (TeamId, AdminUserId) 
            VALUES (@TeamId, @AdminUserId)
            ON DUPLICATE KEY UPDATE 
                AdminUserId = @AdminUserId,
                UpdatedAt = CURRENT_TIMESTAMP";

        using var connection = CreateConnection();
        await connection.ExecuteAsync(query, new { TeamId = teamId, AdminUserId = adminUserId });
    }

    public async Task UpdateReportSettingsAsync(
        string teamId, 
        bool? dailyEnabled = null, 
        bool? weeklyEnabled = null, 
        bool? monthlyEnabled = null)
    {
        var updates = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("TeamId", teamId);

        if (dailyEnabled.HasValue)
        {
            updates.Add("DailyReportEnabled = @DailyEnabled");
            parameters.Add("DailyEnabled", dailyEnabled.Value);
        }

        if (weeklyEnabled.HasValue)
        {
            updates.Add("WeeklyReportEnabled = @WeeklyEnabled");
            parameters.Add("WeeklyEnabled", weeklyEnabled.Value);
        }

        if (monthlyEnabled.HasValue)
        {
            updates.Add("MonthlyReportEnabled = @MonthlyEnabled");
            parameters.Add("MonthlyEnabled", monthlyEnabled.Value);
        }

        if (updates.Count == 0)
            return;

        var query = $"UPDATE WorkspaceSettings SET {string.Join(", ", updates)} WHERE TeamId = @TeamId";

        using var connection = CreateConnection();
        await connection.ExecuteAsync(query, parameters);
    }
}

