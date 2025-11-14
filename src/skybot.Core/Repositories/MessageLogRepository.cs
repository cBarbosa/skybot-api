using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using skybot.Core.Interfaces;
using skybot.Core.Models.Slack;

namespace skybot.Core.Repositories;

public class MessageLogRepository : IMessageLogRepository
{
    private readonly string _connectionString;

    public MessageLogRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySqlConnection") 
            ?? throw new InvalidOperationException("Connection string não configurada");
    }

    private IDbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<int> CreateLogAsync(CreateMessageLogRequest request)
    {
        const string query = @"
            INSERT INTO MessageLogs (
                TeamId, MessageTs, Channel, DestinationType, ThreadTs, 
                ApiKeyId, ApiKeyName, SourceIp, ForwardedFor, UserAgent, Referer, RequestId,
                ContentType, HasAttachments, Status
            ) VALUES (
                @TeamId, @MessageTs, @Channel, @DestinationType, @ThreadTs, 
                @ApiKeyId, @ApiKeyName, @SourceIp, @ForwardedFor, @UserAgent, @Referer, @RequestId,
                @ContentType, @HasAttachments, 'SENT'
            );
            SELECT LAST_INSERT_ID();";

        using var connection = CreateConnection();
        var id = await connection.ExecuteScalarAsync<int>(query, new
        {
            request.TeamId,
            request.MessageTs,
            request.Channel,
            DestinationType = request.DestinationType.ToString(),
            request.ThreadTs,
            request.ApiKeyId,
            request.ApiKeyName,
            request.SourceIp,
            request.ForwardedFor,
            request.UserAgent,
            request.Referer,
            request.RequestId,
            ContentType = request.ContentType.ToString(),
            request.HasAttachments
        });

        return id;
    }

    public async Task<MessageLog?> GetByMessageTsAsync(string messageTs)
    {
        const string query = "SELECT * FROM MessageLogs WHERE MessageTs = @MessageTs LIMIT 1";
        
        using var connection = CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<MessageLog>(query, new { MessageTs = messageTs });
    }

    public async Task<bool> MarkAsDeletedAsync(string messageTs)
    {
        const string query = @"
            UPDATE MessageLogs 
            SET Status = 'DELETED', DeletedAt = NOW() 
            WHERE MessageTs = @MessageTs";

        using var connection = CreateConnection();
        var affected = await connection.ExecuteAsync(query, new { MessageTs = messageTs });
        return affected > 0;
    }

    public async Task<bool> MarkAsFailedAsync(string messageTs, string errorMessage)
    {
        const string query = @"
            UPDATE MessageLogs 
            SET Status = 'FAILED', ErrorMessage = @ErrorMessage 
            WHERE MessageTs = @MessageTs";

        using var connection = CreateConnection();
        var affected = await connection.ExecuteAsync(query, new { MessageTs = messageTs, ErrorMessage = errorMessage });
        return affected > 0;
    }

    public async Task<List<MessageLog>> GetLogsByTeamAsync(string teamId, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT * FROM MessageLogs 
            WHERE TeamId = @TeamId 
            AND SentAt BETWEEN @StartDate AND @EndDate
            ORDER BY SentAt DESC";

        using var connection = CreateConnection();
        var logs = await connection.QueryAsync<MessageLog>(query, new 
        { 
            TeamId = teamId, 
            StartDate = startDate, 
            EndDate = endDate 
        });
        
        return logs.ToList();
    }

    public async Task<Dictionary<string, int>> GetDailyStatsAsync(string teamId, DateTime date)
    {
        const string query = @"
            SELECT 
                DestinationType,
                COUNT(*) as Count
            FROM MessageLogs
            WHERE TeamId = @TeamId
            AND DATE(SentAt) = DATE(@Date)
            AND Status = 'SENT'
            GROUP BY DestinationType";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(string DestinationType, int Count)>(
            query, 
            new { TeamId = teamId, Date = date }
        );

        return results.ToDictionary(r => r.DestinationType, r => r.Count);
    }

    public async Task<Dictionary<string, int>> GetWeeklyStatsAsync(string teamId, DateTime startDate)
    {
        var endDate = startDate.AddDays(7);
        
        const string query = @"
            SELECT 
                DATE(SentAt) as Date,
                COUNT(*) as Count
            FROM MessageLogs
            WHERE TeamId = @TeamId
            AND SentAt BETWEEN @StartDate AND @EndDate
            AND Status = 'SENT'
            GROUP BY DATE(SentAt)
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
                DAY(SentAt) as Day,
                COUNT(*) as Count
            FROM MessageLogs
            WHERE TeamId = @TeamId
            AND YEAR(SentAt) = @Year
            AND MONTH(SentAt) = @Month
            AND Status = 'SENT'
            GROUP BY DAY(SentAt)
            ORDER BY Day";

        using var connection = CreateConnection();
        var results = await connection.QueryAsync<(int Day, int Count)>(
            query, 
            new { TeamId = teamId, Year = year, Month = month }
        );

        return results.ToDictionary(r => r.Day.ToString(), r => r.Count);
    }

    public async Task<List<MessageLog>> GetAuditLogsAsync(
        string teamId, 
        string? ip, 
        string? apiKeyName,
        DateTime startDate, 
        DateTime endDate)
    {
        var conditions = new List<string> { "TeamId = @TeamId", "SentAt BETWEEN @StartDate AND @EndDate" };
        var parameters = new DynamicParameters();
        parameters.Add("TeamId", teamId);
        parameters.Add("StartDate", startDate);
        parameters.Add("EndDate", endDate);

        if (!string.IsNullOrEmpty(ip))
        {
            conditions.Add("(SourceIp = @Ip OR ForwardedFor LIKE @IpLike)");
            parameters.Add("Ip", ip);
            parameters.Add("IpLike", $"%{ip}%");
        }

        if (!string.IsNullOrEmpty(apiKeyName))
        {
            conditions.Add("ApiKeyName = @ApiKeyName");
            parameters.Add("ApiKeyName", apiKeyName);
        }

        var query = $@"
            SELECT * FROM MessageLogs 
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY SentAt DESC
            LIMIT 1000";

        using var connection = CreateConnection();
        var logs = await connection.QueryAsync<MessageLog>(query, parameters);
        return logs.ToList();
    }
}

