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
        
        // IMPORTANTE: Garante que o DateTime seja tratado como UTC
        // O MySQL pode interpretar DateTime como Local se o Kind não for UTC
        DateTime dateToSave = dueDate.Kind == DateTimeKind.Utc 
            ? dueDate 
            : DateTime.SpecifyKind(dueDate, DateTimeKind.Utc);
        
        var sql = @"INSERT INTO Reminders (TeamId, UserId, Message, DueDate, ChannelId, IsSent) 
                    VALUES (@TeamId, @UserId, @Message, @DueDate, @ChannelId, @IsSent);
                    SELECT LAST_INSERT_ID();";
        
        var id = await connection.QuerySingleAsync<int>(sql, new
        {
            TeamId = teamId,
            UserId = userId,
            Message = message,
            DueDate = dateToSave,  // Garante que seja UTC
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

        // Garante que o parâmetro seja UTC
        var utcParam = utcBefore.Kind == DateTimeKind.Utc 
            ? utcBefore 
            : DateTime.SpecifyKind(utcBefore, DateTimeKind.Utc);

        // Filtra apenas lembretes NÃO enviados e com data vencida
        // IMPORTANTE: Verifica se DueDate não é NULL antes de comparar
        var sql = @"SELECT * FROM Reminders 
                    WHERE DueDate IS NOT NULL
                      AND DueDate <= @UtcBefore 
                      AND (IsSent = FALSE OR IsSent IS NULL)
                    ORDER BY DueDate ASC";

        var reminders = await connection.QueryAsync<Reminder>(sql, new { UtcBefore = utcParam });
        return reminders.ToList();
    }

    // Retorna lembretes pendentes com seus AccessTokens em uma única query usando JOIN
    public async Task<List<(Reminder Reminder, string? AccessToken)>> GetPendingRemindersWithTokensAsync(DateTime utcBefore)
    {
        await using var connection = new MySqlConnection(_connectionString);

        // Garante que o parâmetro seja UTC
        var utcParam = utcBefore.Kind == DateTimeKind.Utc 
            ? utcBefore 
            : DateTime.SpecifyKind(utcBefore, DateTimeKind.Utc);

        // Faz JOIN para trazer o AccessToken junto com os lembretes em uma única query
        var sql = @"SELECT r.Id, r.TeamId, r.ChannelId, r.UserId, r.Message, r.DueDate, r.IsSent, r.SentAt,
                           t.AccessToken
                    FROM Reminders r
                    LEFT JOIN SlackTokens t ON r.TeamId = t.TeamId
                    WHERE r.DueDate IS NOT NULL
                      AND r.DueDate <= @UtcBefore 
                      AND (r.IsSent = FALSE OR r.IsSent IS NULL)
                    ORDER BY r.DueDate ASC";

        var results = await connection.QueryAsync(
            sql,
            (Reminder reminder, string? accessToken) => (reminder, accessToken),
            new { UtcBefore = utcParam },
            splitOn: "AccessToken");

        return results.ToList();
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

    public async Task UpdateReminderTeamIdAsync(int reminderId, string correctTeamId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.ExecuteAsync(
            "UPDATE Reminders SET TeamId = @TeamId WHERE Id = @Id",
            new { Id = reminderId, TeamId = correctTeamId });
    }
}

