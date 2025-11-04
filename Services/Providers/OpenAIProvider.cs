using System.Net.Http.Headers;
using System.Text.Json;

namespace skybot.Services.Providers;

internal class OpenAIProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _model;

    public string Name => "OpenAI";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public OpenAIProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _apiKey = configuration["OpenAI:ApiKey"];
        _model = configuration["OpenAI:Model"] ?? "gpt-3.5-turbo";
    }

    public async Task<string?> GetResponseAsync(string userMessage, string systemPrompt)
    {
        if (!IsConfigured) return null;

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            };

            var payload = new
            {
                model = _model,
                messages = messages,
                max_tokens = 500,
                temperature = 0.7
            };

            var response = await _httpClient.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                JsonContent.Create(payload));

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ERROR] {Name} API error: {response.StatusCode} - {json}");
                return null;
            }

            var result = JsonSerializer.Deserialize<JsonElement>(json);
            
            if (result.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                return choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
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


