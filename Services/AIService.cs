using System.Net.Http.Headers;
using System.Text.Json;

namespace skybot.Services;

internal class AIService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _apiKey;
    private readonly string _model;

    public AIService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["OpenAI:ApiKey"];
        _model = configuration["OpenAI:Model"] ?? "gpt-3.5-turbo";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string?> GetAIResponseAsync(string userMessage, string? context = null)
    {
        if (!IsConfigured)
        {
            Console.WriteLine("[WARNING] OpenAI API Key não configurada");
            return null;
        }

        var client = _httpClientFactory.CreateClient();
        try
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            client.Timeout = TimeSpan.FromSeconds(30);

            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = context ?? "Você é um assistente útil e amigável. Responda de forma clara e concisa em português brasileiro."
                },
                new
                {
                    role = "user",
                    content = userMessage
                }
            };

            var payload = new
            {
                model = _model,
                messages = messages,
                max_tokens = 500,
                temperature = 0.7
            };

            var response = await client.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                JsonContent.Create(payload));

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ERROR] OpenAI API error: {response.StatusCode} - {json}");
                return null;
            }

            var result = JsonSerializer.Deserialize<JsonElement>(json);
            
            if (result.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var message = choices[0].GetProperty("message").GetProperty("content").GetString();
                return message?.Trim();
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exceção ao chamar OpenAI: {ex.Message}");
            Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
            return null;
        }
    }
}

