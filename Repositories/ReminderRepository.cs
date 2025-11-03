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
        
        var sql = @"INSERT INTO Reminders (TeamId, UserId, Message, DueDate, ChannelId, IsSent) 
                    VALUES (@TeamId, @UserId, @Message, @DueDate, @ChannelId, @IsSent);
                    SELECT LAST_INSERT_ID();";
        
        var id = await connection.QuerySingleAsync<int>(sql, new
        {
            TeamId = teamId,
            UserId = userId,
            Message = message,
            DueDate = dueDate,
            ChannelId = channelId,
            IsSent = false
        });
        
        return id;
    }

    public async Task<List<Reminder>> GetRemindersByTeamAsync(string teamId, bool includeSent = false)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var sql = "SELECT * FROM Reminders WHERE TeamId = @TeamId";
        if (!includeSent)
        {
            sql += " AND (IsSent = FALSE OR IsSent IS NULL)";
        }
        sql += " ORDER BY DueDate ASC";
        
        var reminders = await connection.QueryAsync<Reminder>(sql, new { TeamId = teamId });
        return reminders.ToList();
    }

    public async Task<List<Reminder>> GetRemindersByUserAsync(string teamId, string userId, bool includeSent = false)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var sql = "SELECT * FROM Reminders WHERE TeamId = @TeamId AND UserId = @UserId";
        if (!includeSent)
        {
            sql += " AND (IsSent = FALSE OR IsSent IS NULL)";
        }
        sql += " ORDER BY DueDate ASC";
        
        var reminders = await connection.QueryAsync<Reminder>(sql, new { TeamId = teamId, UserId = userId });
        return reminders.ToList();
    }

    public async Task<List<Reminder>> GetPendingRemindersAsync(DateTime utcBefore)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        // Filtra apenas lembretes NÃO enviados e com data vencida
        var sql = @"SELECT * FROM Reminders 
                    WHERE DueDate <= @UtcBefore 
                      AND (IsSent = FALSE OR IsSent IS NULL)
                    ORDER BY DueDate ASC";
        
        var reminders = await connection.QueryAsync<Reminder>(sql, new { UtcBefore = utcBefore });
        return reminders.ToList();
    }

    public async Task MarkReminderAsSentAsync(int reminderId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        // Marca como enviado ao invés de deletar (mantém histórico)
        var sql = @"UPDATE Reminders 
                    SET IsSent = TRUE, SentAt = @SentAt 
                    WHERE Id = @Id";
        await connection.ExecuteAsync(sql, new { Id = reminderId, SentAt = DateTime.UtcNow });
    }
}

