using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using skybot.Core.Interfaces;
using skybot.Core.Models.Commands;

namespace skybot.Core.Repositories;

public class AgentConversationRepository : IAgentConversationRepository
{
    private readonly string _connectionString;

    public AgentConversationRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySqlConnection") 
            ?? throw new InvalidOperationException("Connection string não configurada");
    }

    private IDbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<int> CreateAsync(CreateAgentConversationRequest request)
    {
        const string query = @"
            INSERT INTO AgentConversations (
                ThreadKey, TeamId, Channel, ThreadTs, UserId, 
                ConversationHistory, MessageCount, LastInteractionAt
            ) VALUES (
                @ThreadKey, @TeamId, @Channel, @ThreadTs, @UserId,
                @ConversationHistory, @MessageCount, NOW()
            );
            SELECT LAST_INSERT_ID();";

        var historyJson = JsonSerializer.Serialize(request.History);

        using var connection = CreateConnection();
        return await connection.ExecuteScalarAsync<int>(query, new
        {
            request.ThreadKey,
            request.TeamId,
            request.Channel,
            request.ThreadTs,
            request.UserId,
            ConversationHistory = historyJson,
            MessageCount = request.History.Count
        });
    }

    public async Task<AgentConversation?> GetByThreadKeyAsync(string threadKey)
    {
        const string query = "SELECT * FROM AgentConversations WHERE ThreadKey = @ThreadKey LIMIT 1";
        
        using var connection = CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<AgentConversation>(query, new { ThreadKey = threadKey });
    }

    public async Task<bool> UpdateAsync(UpdateAgentConversationRequest request)
    {
        const string query = @"
            UPDATE AgentConversations 
            SET ConversationHistory = @ConversationHistory,
                SummaryContext = @SummaryContext,
                MessageCount = @MessageCount,
                LastInteractionAt = NOW()
            WHERE ThreadKey = @ThreadKey";

        var historyJson = JsonSerializer.Serialize(request.History);

        using var connection = CreateConnection();
        var affected = await connection.ExecuteAsync(query, new
        {
            request.ThreadKey,
            ConversationHistory = historyJson,
            request.SummaryContext,
            MessageCount = request.History.Count
        });

        return affected > 0;
    }

    public async Task<bool> DeactivateAsync(string threadKey)
    {
        const string query = @"
            UPDATE AgentConversations 
            SET IsActive = FALSE
            WHERE ThreadKey = @ThreadKey";

        using var connection = CreateConnection();
        var affected = await connection.ExecuteAsync(query, new { ThreadKey = threadKey });
        return affected > 0;
    }

    public async Task<List<AgentConversation>> GetActiveConversationsAsync(string teamId)
    {
        const string query = @"
            SELECT * FROM AgentConversations 
            WHERE TeamId = @TeamId 
            AND IsActive = TRUE
            ORDER BY LastInteractionAt DESC";

        using var connection = CreateConnection();
        var conversations = await connection.QueryAsync<AgentConversation>(query, new { TeamId = teamId });
        return conversations.ToList();
    }

    public async Task<bool> CreateSummaryAsync(string threadKey, string summary)
    {
        const string query = @"
            UPDATE AgentConversations 
            SET SummaryContext = @Summary
            WHERE ThreadKey = @ThreadKey";

        using var connection = CreateConnection();
        var affected = await connection.ExecuteAsync(query, new { ThreadKey = threadKey, Summary = summary });
        return affected > 0;
    }
}

