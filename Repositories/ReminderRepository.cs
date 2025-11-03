using Dapper;
using MySqlConnector;
using skybot.Models;

namespace skybot.Repositories;

internal class ReminderRepository
{
    private readonly string _connectionString;

    public ReminderRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySqlConnection") 
            ?? throw new InvalidOperationException("Connection string não configurada");
    }

    public async Task<int> CreateReminderAsync(string teamId, string userId, string message, DateTime dueDate, string? channelId = null)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var sql = @"INSERT INTO Reminders (TeamId, UserId, Message, DueDate, ChannelId) 
                    VALUES (@TeamId, @UserId, @Message, @DueDate, @ChannelId);
                    SELECT LAST_INSERT_ID();";
        
        var id = await connection.QuerySingleAsync<int>(sql, new
        {
            TeamId = teamId,
            UserId = userId,
            Message = message,
            DueDate = dueDate,
            ChannelId = channelId
        });
        
        return id;
    }

    public async Task<List<Reminder>> GetRemindersByTeamAsync(string teamId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var sql = "SELECT * FROM Reminders WHERE TeamId = @TeamId ORDER BY DueDate ASC";
        
        var reminders = await connection.QueryAsync<Reminder>(sql, new { TeamId = teamId });
        return reminders.ToList();
    }

    public async Task<List<Reminder>> GetRemindersByUserAsync(string teamId, string userId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var sql = "SELECT * FROM Reminders WHERE TeamId = @TeamId AND UserId = @UserId ORDER BY DueDate ASC";
        
        var reminders = await connection.QueryAsync<Reminder>(sql, new { TeamId = teamId, UserId = userId });
        return reminders.ToList();
    }
}

