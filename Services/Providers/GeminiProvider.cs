using System.Text.Json;

namespace skybot.Services.Providers;

internal class GeminiProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _model;

    public string Name => "Google Gemini";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public GeminiProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _apiKey = configuration["Gemini:ApiKey"];
        _model = configuration["Gemini:Model"] ?? "gemini-pro";
    }

    public async Task<string?> GetResponseAsync(string userMessage, string systemPrompt)
    {
        if (!IsConfigured) return null;

        try
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // O Gemini usa um formato diferente - combina system prompt com user message
            var fullPrompt = $"{systemPrompt}\n\nUsuário: {userMessage}\nAssistente:";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = fullPrompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    maxOutputTokens = 500
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            var response = await _httpClient.PostAsync(url, JsonContent.Create(payload));

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ERROR] {Name} API error: {response.StatusCode} - {json}");
                return null;
            }

            var result = JsonSerializer.Deserialize<JsonElement>(json);
            
            if (result.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var content = candidates[0].GetProperty("content");
                if (content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                {
                    return parts[0].GetProperty("text").GetString()?.Trim();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exceção ao chamar {Name}: {ex.Message}");
            return null;
        }
    }
}


