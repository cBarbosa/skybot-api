namespace skybot.Core.Models.Commands;

/// <summary>
/// Representa uma interação registrada com o agente IA
/// </summary>
public record AgentInteraction(
    int Id,
    string TeamId,
    string UserId,
    string ThreadKey,
    string Channel,
    string? ThreadTs,
    string MessageTs,
    int UserMessageLength,
    string AIProvider,
    string? AIModel,
    int ResponseLength,
    int ResponseTime,
    string? SourceIp,
    string? UserAgent,
    bool Success,
    string? ErrorMessage,
    int? TokensUsed,
    decimal? EstimatedCost,
    DateTime InteractedAt
);

/// <summary>
/// Request para criar uma nova interação com o agente
/// </summary>
public record CreateAgentInteractionRequest(
    string TeamId,
    string UserId,
    string ThreadKey,
    string Channel,
    string? ThreadTs,
    string MessageTs,
    int UserMessageLength,
    string AIProvider,
    string? AIModel,
    int ResponseLength,
    int ResponseTime,
    string? SourceIp,
    string? UserAgent,
    bool Success = true,
    string? ErrorMessage = null,
    int? TokensUsed = null,
    decimal? EstimatedCost = null
);

