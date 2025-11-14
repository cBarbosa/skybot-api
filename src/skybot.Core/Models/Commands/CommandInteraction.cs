namespace skybot.Core.Models.Commands;

/// <summary>
/// Tipos de interação com o bot
/// </summary>
public enum InteractionType
{
    COMMAND,
    BUTTON,
    MODAL
}

/// <summary>
/// Representa uma interação registrada (comando, botão ou modal)
/// </summary>
public record CommandInteraction(
    int Id,
    string TeamId,
    string UserId,
    InteractionType InteractionType,
    string? Command,
    string? ActionId,
    string? Arguments,
    string Channel,
    string? ThreadTs,
    string MessageTs,
    string? SourceIp,
    string? UserAgent,
    bool Success,
    string? ErrorMessage,
    DateTime ExecutedAt
);

/// <summary>
/// Request para criar uma nova interação
/// </summary>
public record CreateCommandInteractionRequest(
    string TeamId,
    string UserId,
    InteractionType InteractionType,
    string? Command,
    string? ActionId,
    string? Arguments,
    string Channel,
    string? ThreadTs,
    string MessageTs,
    string? SourceIp,
    string? UserAgent,
    bool Success = true,
    string? ErrorMessage = null
);

