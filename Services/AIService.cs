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
                    var response = await preferredProvider.GetResponseAsync(userMessage, systemPrompt);
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        Console.WriteLine($"[SUCCESS] Resposta obtida de {preferredProvider.Name} (thread {threadKey})");
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
                var response = await provider.GetResponseAsync(userMessage, systemPrompt);
                
                if (!string.IsNullOrWhiteSpace(response))
                {
                    Console.WriteLine($"[SUCCESS] Resposta obtida de {provider.Name}");
                    
                    // Se tem threadKey, armazena o provider que funcionou
                    if (!string.IsNullOrEmpty(threadKey))
                    {
                        _threadProviders.AddOrUpdate(threadKey, provider.Name, (key, oldValue) => provider.Name);
                        Console.WriteLine($"[INFO] Provider {provider.Name} associado à thread {threadKey}");
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
    
    // Método para limpar provider de uma thread (útil quando desativa modo agente virtual)
    public static void ClearThreadProvider(string threadKey)
    {
        _threadProviders.TryRemove(threadKey, out _);
    }
}

