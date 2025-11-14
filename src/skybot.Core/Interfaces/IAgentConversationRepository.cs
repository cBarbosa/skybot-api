using skybot.Core.Models.Commands;

namespace skybot.Core.Interfaces;

public interface IAgentConversationRepository
{
    /// <summary>
    /// Cria uma nova conversa
    /// </summary>
    Task<int> CreateAsync(CreateAgentConversationRequest request);
    
    /// <summary>
    /// Obtém uma conversa pela chave da thread
    /// </summary>
    Task<AgentConversation?> GetByThreadKeyAsync(string threadKey);
    
    /// <summary>
    /// Atualiza o histórico de uma conversa existente
    /// </summary>
    Task<bool> UpdateAsync(UpdateAgentConversationRequest request);
    
    /// <summary>
    /// Desativa uma conversa (marca IsActive = false)
    /// </summary>
    Task<bool> DeactivateAsync(string threadKey);
    
    /// <summary>
    /// Obtém todas as conversas ativas de um team
    /// </summary>
    Task<List<AgentConversation>> GetActiveConversationsAsync(string teamId);
    
    /// <summary>
    /// Cria ou atualiza o resumo de contexto de uma conversa
    /// </summary>
    Task<bool> CreateSummaryAsync(string threadKey, string summary);
}

