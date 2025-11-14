namespace skybot.Core.Models.Commands;

/// <summary>
/// Representa uma conversa armazenada com o agente IA
/// ThreadKey formato: TeamId_UserId_Channel_ThreadTs (máx 191 chars)
/// </summary>
public record AgentConversation(
    int Id,
    string ThreadKey,
    string TeamId,
    string Channel,
    string? ThreadTs,
    string UserId,
    string ConversationHistory,
    string? SummaryContext,
    int MessageCount,
    DateTime StartedAt,
    DateTime LastInteractionAt,
    DateTime UpdatedAt,
    bool IsActive
);

/// <summary>
/// Request para criar uma nova conversa
/// </summary>
public record CreateAgentConversationRequest(
    string ThreadKey,
    string TeamId,
    string Channel,
    string? ThreadTs,
    string UserId,
    List<(string Role, string Content)> History
);

/// <summary>
/// Request para atualizar uma conversa existente
/// </summary>
public record UpdateAgentConversationRequest(
    string ThreadKey,
    List<(string Role, string Content)> History,
    string? SummaryContext = null
);

