using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using skybot.Core.Helpers;
using skybot.Core.Interfaces;
using skybot.Core.Models.Commands;
using skybot.Core.Providers;

namespace skybot.Core.Services;

public class AIService : IAIService
{
    private readonly List<IAIProvider> _providers;
    private readonly string _defaultSystemPrompt;
    private readonly IAgentConversationRepository _conversationRepo;
    private readonly IAgentInteractionRepository _interactionRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    // Cache em memória para fallback rápido
    // Chave: ThreadKey, Valor: Nome do Provider
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _threadProviders = 
        new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
    
    // Cache em memória para histórico (usado se o banco falhar)
    // Chave: ThreadKey, Valor: Lista de mensagens (Role, Content)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<(string Role, string Content)>> _conversationHistory = 
        new System.Collections.Concurrent.ConcurrentDictionary<string, List<(string Role, string Content)>>();

    public AIService(
        IHttpClientFactory httpClientFactory, 
        IConfiguration configuration,
        IAgentConversationRepository conversationRepo,
        IAgentInteractionRepository interactionRepo,
        IHttpContextAccessor httpContextAccessor)
    {
        _defaultSystemPrompt = configuration["OpenAI:SystemPrompt"] ?? 
            configuration["Gemini:SystemPrompt"] ??
            "Você é um assistente útil e amigável. Responda de forma clara e concisa em português brasileiro.";

        // Ordem de prioridade: primeiro OpenAI, depois Gemini
        _providers = new List<IAIProvider>
        {
            new OpenAIProvider(httpClientFactory, configuration),
            new GeminiProvider(httpClientFactory, configuration)
        };
        
        _conversationRepo = conversationRepo;
        _interactionRepo = interactionRepo;
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsConfigured => _providers.Any(p => p.IsConfigured);

    // Método original mantido para compatibilidade
    public async Task<string?> GetAIResponseAsync(string userMessage, string? context = null)
    {
        return await GetAIResponseAsync(userMessage, context, null);
    }

    // Método sobrecarregado que aceita threadKey para manter consistência
    public async Task<string?> GetAIResponseAsync(string userMessage, string? context, string? threadKey)
    {
        if (!IsConfigured)
        {
            Console.WriteLine("[WARNING] Nenhuma API de IA configurada");
            return null;
        }

        var startTime = DateTime.UtcNow;
        var systemPrompt = context ?? _defaultSystemPrompt;
        List<(string, string)>? history = null;
        string? aiProvider = null;
        string? response = null;
        bool success = false;
        string? errorMessage = null;

        try
        {
            // Carrega histórico (primeiro do banco, depois do cache se falhar)
            if (!string.IsNullOrEmpty(threadKey))
            {
                history = await LoadConversationHistoryAsync(threadKey);
            }

            // Se tem threadKey, verifica se já tem um provider associado
            IAIProvider? preferredProvider = null;
            if (!string.IsNullOrEmpty(threadKey) && _threadProviders.TryGetValue(threadKey, out var providerName))
            {
                preferredProvider = _providers.FirstOrDefault(p => p.Name == providerName && p.IsConfigured);
                if (preferredProvider != null)
                {
                    Console.WriteLine($"[INFO] Thread {threadKey} usa provider preferencial: {preferredProvider.Name}");
                    try
                    {
                        response = await preferredProvider.GetResponseAsync(userMessage, systemPrompt, history);
                        if (!string.IsNullOrWhiteSpace(response))
                        {
                            Console.WriteLine($"[SUCCESS] Resposta obtida de {preferredProvider.Name} (thread {threadKey})");
                            aiProvider = preferredProvider.Name;
                            success = true;
                        }
                        else
                        {
                            Console.WriteLine($"[WARNING] Provider preferencial {preferredProvider.Name} retornou resposta vazia");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARNING] Erro ao usar provider preferencial {preferredProvider.Name}: {ex.Message}");
                        errorMessage = ex.Message;
                        _threadProviders.TryRemove(threadKey, out _);
                    }
                }
            }

            // Se não conseguiu com o preferencial, tenta cada provedor em ordem
            if (!success)
            {
                foreach (var provider in _providers)
                {
                    if (preferredProvider != null && provider == preferredProvider)
                        continue;
                        
                    if (!provider.IsConfigured)
                    {
                        Console.WriteLine($"[INFO] {provider.Name} não configurado, pulando...");
                        continue;
                    }

                    try
                    {
                        Console.WriteLine($"[INFO] Tentando {provider.Name}...");
                        response = await provider.GetResponseAsync(userMessage, systemPrompt, history);
                        
                        if (!string.IsNullOrWhiteSpace(response))
                        {
                            Console.WriteLine($"[SUCCESS] Resposta obtida de {provider.Name}");
                            aiProvider = provider.Name;
                            success = true;
                            
                            // Armazena o provider que funcionou
                            if (!string.IsNullOrEmpty(threadKey))
                            {
                                _threadProviders.AddOrUpdate(threadKey, provider.Name, (key, oldValue) => provider.Name);
                            }
                            
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARNING] Erro ao usar {provider.Name}: {ex.Message}");
                        errorMessage = ex.Message;
                    }
                }
            }

            if (success && history != null && response != null && !string.IsNullOrEmpty(threadKey))
            {
                // Adiciona ao histórico
                history.Add(("user", userMessage));
                history.Add(("assistant", response));

                // Verifica se precisa resumir (mais de 20 mensagens)
                string? summary = null;
                if (history.Count > 20)
                {
                    summary = await CreateConversationSummaryAsync(history, aiProvider!);
                    // Mantém apenas as últimas 10 mensagens + resumo
                    history.RemoveRange(0, history.Count - 10);
                }

                // Persiste no banco (não-bloqueante)
                await SaveConversationAsync(threadKey, history, summary);
            }

            return response;
        }
        finally
        {
            // Registra a interação para métricas (não-bloqueante)
            if (!string.IsNullOrEmpty(threadKey))
            {
                await LogAgentInteractionAsync(
                    threadKey, userMessage, response, aiProvider, 
                    startTime, success, errorMessage);
            }
        }
    }
    
    // Método para limpar provider e histórico de uma thread
    public void ClearThreadProvider(string threadKey)
    {
        _threadProviders.TryRemove(threadKey, out _);
        _conversationHistory.TryRemove(threadKey, out _);
        Console.WriteLine($"[INFO] Provider e histórico limpos para thread {threadKey}");
        
        // Também desativa no banco
        Task.Run(async () =>
        {
            try
            {
                await _conversationRepo.DeactivateAsync(threadKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Falha ao desativar conversa no banco: {ex.Message}");
            }
        });
    }

    private async Task<List<(string, string)>> LoadConversationHistoryAsync(string threadKey)
    {
        try
        {
            // Tenta carregar do banco primeiro
            var conversation = await _conversationRepo.GetByThreadKeyAsync(threadKey);
            if (conversation != null && conversation.IsActive)
            {
                var history = JsonSerializer.Deserialize<List<(string, string)>>(conversation.ConversationHistory);
                if (history != null)
                {
                    Console.WriteLine($"[INFO] Histórico carregado do banco para thread {threadKey}: {history.Count} mensagens");
                    // Atualiza o cache
                    _conversationHistory.AddOrUpdate(threadKey, history, (key, oldValue) => history);
                    return history;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING] Falha ao carregar histórico do banco: {ex.Message}");
        }

        // Fallback para o cache em memória
        var cachedHistory = _conversationHistory.GetOrAdd(threadKey, _ => new List<(string, string)>());
        Console.WriteLine($"[INFO] Usando histórico do cache para thread {threadKey}: {cachedHistory.Count} mensagens");
        return cachedHistory;
    }

    private async Task SaveConversationAsync(string threadKey, List<(string, string)> history, string? summary)
    {
        try
        {
            // Atualiza o cache primeiro (sempre funciona)
            _conversationHistory.AddOrUpdate(threadKey, history, (key, oldValue) => history);

            // Extrai informações do threadKey (formato: TeamId_User_Channel_ThreadTs)
            var parts = threadKey.Split('_');
            if (parts.Length < 3) return;

            var teamId = parts[0];
            var userId = parts[1];
            var channel = parts[2];
            var threadTs = parts.Length > 3 ? parts[3] : null;

            var existing = await _conversationRepo.GetByThreadKeyAsync(threadKey);
            
            if (existing == null)
            {
                var request = new CreateAgentConversationRequest(
                    ThreadKey: threadKey,
                    TeamId: teamId,
                    Channel: channel,
                    ThreadTs: threadTs,
                    UserId: userId,
                    History: history
                );
                await _conversationRepo.CreateAsync(request);
                Console.WriteLine($"[INFO] Nova conversa criada no banco para thread {threadKey}");
            }
            else
            {
                var updateRequest = new UpdateAgentConversationRequest(
                    ThreadKey: threadKey,
                    History: history,
                    SummaryContext: summary
                );
                await _conversationRepo.UpdateAsync(updateRequest);
                Console.WriteLine($"[INFO] Conversa atualizada no banco para thread {threadKey}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Falha ao salvar conversa no banco: {ex.Message}");
            // Não propaga o erro - cache em memória já foi atualizado
        }
    }

    private async Task<string?> CreateConversationSummaryAsync(List<(string, string)> history, string providerName)
    {
        try
        {
            var summaryPrompt = "Resuma esta conversa de forma concisa, capturando os pontos principais e o contexto necessário para continuar a conversa:";
            var conversationText = string.Join("\n", history.Take(history.Count - 10).Select(h => $"{h.Item1}: {h.Item2}"));
            
            var selectedProvider = _providers.FirstOrDefault(p => p.Name == providerName && p.IsConfigured);
            if (selectedProvider != null)
            {
                var summary = await selectedProvider.GetResponseAsync(conversationText, summaryPrompt, null);
                Console.WriteLine($"[INFO] Resumo criado com sucesso usando {providerName}");
                return summary;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Falha ao criar resumo: {ex.Message}");
        }
        return null;
    }

    private async Task LogAgentInteractionAsync(
        string threadKey, 
        string userMessage, 
        string? response,
        string? aiProvider,
        DateTime startTime,
        bool success,
        string? errorMessage)
    {
        try
        {
            var responseTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var parts = threadKey.Split('_');
            if (parts.Length < 3) return;

            var teamId = parts[0];
            var userId = parts[1];
            var channel = parts[2];
            var threadTs = parts.Length > 3 ? parts[3] : null;

            var httpContext = _httpContextAccessor.HttpContext;
            var request = new CreateAgentInteractionRequest(
                TeamId: teamId,
                UserId: userId,
                ThreadKey: threadKey,
                Channel: channel,
                ThreadTs: threadTs,
                MessageTs: DateTime.UtcNow.Ticks.ToString(),
                UserMessageLength: userMessage.Length,
                AIProvider: aiProvider ?? "unknown",
                AIModel: null, // Pode ser extraído do provider futuramente
                ResponseLength: response?.Length ?? 0,
                ResponseTime: responseTime,
                SourceIp: httpContext != null ? HttpContextHelper.GetSourceIp(httpContext) : null,
                UserAgent: httpContext != null ? HttpContextHelper.GetUserAgent(httpContext) : null,
                Success: success,
                ErrorMessage: errorMessage
            );

            await _interactionRepo.CreateAsync(request);
            Console.WriteLine($"[INFO] Interação com agente registrada: {responseTime}ms, success={success}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Falha ao registrar interação com agente: {ex.Message}");
            // Não propaga o erro para não afetar a funcionalidade principal
        }
    }
}
