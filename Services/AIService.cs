using skybot.Services.Providers;

namespace skybot.Services;

internal class AIService
{
    private readonly List<IAIProvider> _providers;
    private readonly string _defaultSystemPrompt;

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

    public async Task<string?> GetAIResponseAsync(string userMessage, string? context = null)
    {
        if (!IsConfigured)
        {
            Console.WriteLine("[WARNING] Nenhuma API de IA configurada");
            return null;
        }

        var systemPrompt = context ?? _defaultSystemPrompt;

        // Tenta cada provedor em ordem até um funcionar
        foreach (var provider in _providers)
        {
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
}

