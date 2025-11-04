using skybot.Services.Providers;

namespace skybot.Services;

internal class AIService
{
    private readonly List<IAIProvider> _providers;
    private readonly string _defaultSystemPrompt;
    
    // Cache para armazenar qual provider foi usado por thread
    // Chave: ThreadKey, Valor: Nome do Provider
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _threadProviders = 
        new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
    
    // Cache para armazenar histórico de conversas por thread
    // Chave: ThreadKey, Valor: Lista de mensagens (Role, Content)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<(string Role, string Content)>> _conversationHistory = 
        new System.Collections.Concurrent.ConcurrentDictionary<string, List<(string Role, string Content)>>();

    public AIService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
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

        var systemPrompt = context ?? _defaultSystemPrompt;
        
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
                    // Obtém histórico de conversa para esta thread
                    var history = _conversationHistory.GetOrAdd(threadKey, _ => new List<(string, string)>());
                    
                    var response = await preferredProvider.GetResponseAsync(userMessage, systemPrompt, history);
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        Console.WriteLine($"[SUCCESS] Resposta obtida de {preferredProvider.Name} (thread {threadKey})");
                        
                        // Adiciona mensagem do usuário e resposta da IA ao histórico
                        history.Add(("user", userMessage));
                        history.Add(("assistant", response));
                        
                        // Limita o histórico a 20 mensagens (10 trocas) para não exceder limites de tokens
                        if (history.Count > 20)
                        {
                            history.RemoveRange(0, history.Count - 20);
                        }
                        
                        return response;
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] Provider preferencial {preferredProvider.Name} retornou resposta vazia para thread {threadKey}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] Erro ao usar provider preferencial {preferredProvider.Name} na thread {threadKey}: {ex.Message}");
                    // Remove o provider preferencial se falhou, para tentar outro na próxima vez
                    _threadProviders.TryRemove(threadKey, out _);
                }
            }
        }

        // Tenta cada provedor em ordem até um funcionar
        foreach (var provider in _providers)
        {
            // Se já tentou o provider preferencial, pula
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
                
                // Obtém histórico de conversa se tem threadKey
                List<(string, string)>? history = null;
                if (!string.IsNullOrEmpty(threadKey))
                {
                    history = _conversationHistory.GetOrAdd(threadKey, _ => new List<(string, string)>());
                }
                
                var response = await provider.GetResponseAsync(userMessage, systemPrompt, history);
                
                if (!string.IsNullOrWhiteSpace(response))
                {
                    Console.WriteLine($"[SUCCESS] Resposta obtida de {provider.Name}");
                    
                    // Se tem threadKey, armazena o provider que funcionou e atualiza histórico
                    if (!string.IsNullOrEmpty(threadKey))
                    {
                        _threadProviders.AddOrUpdate(threadKey, provider.Name, (key, oldValue) => provider.Name);
                        Console.WriteLine($"[INFO] Provider {provider.Name} associado à thread {threadKey}");
                        
                        // Adiciona mensagem do usuário e resposta da IA ao histórico
                        var threadHistory = _conversationHistory.GetOrAdd(threadKey, _ => new List<(string, string)>());
                        threadHistory.Add(("user", userMessage));
                        threadHistory.Add(("assistant", response));
                        
                        // Limita o histórico a 20 mensagens (10 trocas) para não exceder limites de tokens
                        if (threadHistory.Count > 20)
                        {
                            threadHistory.RemoveRange(0, threadHistory.Count - 20);
                        }
                    }
                    
                    return response;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Erro ao usar {provider.Name}: {ex.Message}");
                // Continua para o próximo provedor
            }
        }

        Console.WriteLine("[ERROR] Todos os provedores de IA falharam");
        return null;
    }
    
    // Método para limpar provider e histórico de uma thread (útil quando desativa modo agente virtual)
    public static void ClearThreadProvider(string threadKey)
    {
        _threadProviders.TryRemove(threadKey, out _);
        _conversationHistory.TryRemove(threadKey, out _);
        Console.WriteLine($"[INFO] Provider e histórico limpos para thread {threadKey}");
    }
}

