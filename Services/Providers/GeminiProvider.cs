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
        return await GetResponseAsync(userMessage, systemPrompt, null);
    }

    public async Task<string?> GetResponseAsync(string userMessage, string systemPrompt, List<(string Role, string Content)>? conversationHistory)
    {
        if (!IsConfigured)
        {
            Console.WriteLine("[DEBUG] GeminiProvider: Não configurado (ApiKey ausente)");
            return null;
        }

        try
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Constrói o histórico de conversa para Gemini
            var contents = new List<object>();

            // Adiciona histórico de conversa se existir
            if (conversationHistory != null && conversationHistory.Count > 0)
            {
                foreach (var (role, content) in conversationHistory)
                {
                    var geminiRole = role == "user" ? "user" : "model";
                    contents.Add(new
                    {
                        role = geminiRole,
                        parts = new[]
                        {
                            new { text = content }
                        }
                    });
                }
            }

            // Adiciona a mensagem atual do usuário
            contents.Add(new
            {
                role = "user",
                parts = new[]
                {
                    new { text = userMessage }
                }
            });

            var payload = new
            {
                contents = contents.ToArray(),
                systemInstruction = new
                {
                    parts = new[]
                    {
                        new { text = systemPrompt }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    maxOutputTokens = 500
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            
            Console.WriteLine($"[DEBUG] GeminiProvider: Enviando requisição para modelo {_model}");
            Console.WriteLine($"[DEBUG] GeminiProvider: URL: {url.Replace(_apiKey ?? "", "***")}");
            Console.WriteLine($"[DEBUG] GeminiProvider: User message length: {userMessage.Length} caracteres");
            Console.WriteLine($"[DEBUG] GeminiProvider: Conversation history items: {(conversationHistory?.Count ?? 0)}");
            
            var response = await _httpClient.PostAsync(url, JsonContent.Create(payload));

            var json = await response.Content.ReadAsStringAsync();
            
            Console.WriteLine($"[DEBUG] GeminiProvider: Status Code: {response.StatusCode}");
            Console.WriteLine($"[DEBUG] GeminiProvider: Response length: {json.Length} caracteres");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ERROR] GeminiProvider: API error - Status: {response.StatusCode}");
                Console.WriteLine($"[ERROR] GeminiProvider: Response body: {json}");
                
                try
                {
                    var errorJson = JsonSerializer.Deserialize<JsonElement>(json);
                    if (errorJson.TryGetProperty("error", out var error))
                    {
                        var message = error.TryGetProperty("message", out var msg) ? msg.GetString() : "N/A";
                        var code = error.TryGetProperty("code", out var errCode) ? errCode.GetInt32() : 0;
                        Console.WriteLine($"[DEBUG] GeminiProvider: Error code: {code}, message: {message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG] GeminiProvider: Erro ao parsear resposta de erro: {ex.Message}");
                }
                
                return null;
            }

            var result = JsonSerializer.Deserialize<JsonElement>(json);
            
            Console.WriteLine($"[DEBUG] GeminiProvider: Resposta parseada com sucesso");
            
            if (result.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];
                Console.WriteLine($"[DEBUG] GeminiProvider: Número de candidatos: {candidates.GetArrayLength()}");
                
                // Verifica se há finishReason
                if (candidate.TryGetProperty("finishReason", out var finishReason))
                {
                    var reason = finishReason.GetString();
                    Console.WriteLine($"[DEBUG] GeminiProvider: Finish reason: {reason}");
                    
                    if (reason != "STOP")
                    {
                        Console.WriteLine($"[WARNING] GeminiProvider: Finish reason não é STOP: {reason}");
                    }
                }
                
                // Verifica se há safetyRatings
                if (candidate.TryGetProperty("safetyRatings", out var safetyRatings))
                {
                    Console.WriteLine($"[DEBUG] GeminiProvider: Safety ratings encontrados");
                    foreach (var rating in safetyRatings.EnumerateArray())
                    {
                        if (rating.TryGetProperty("category", out var category) && rating.TryGetProperty("probability", out var probability))
                        {
                            Console.WriteLine($"[DEBUG] GeminiProvider: Safety - Category: {category.GetString()}, Probability: {probability.GetString()}");
                        }
                    }
                }
                
                if (candidate.TryGetProperty("content", out var content))
                {
                    if (content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                    {
                        var text = parts[0].GetProperty("text").GetString()?.Trim();
                        Console.WriteLine($"[DEBUG] GeminiProvider: Texto extraído, length: {text?.Length ?? 0} caracteres");
                        return text;
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] GeminiProvider: Nenhuma parte encontrada no content");
                    }
                }
                else
                {
                    Console.WriteLine($"[DEBUG] GeminiProvider: Property 'content' não encontrada no candidato");
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] GeminiProvider: Nenhum candidato encontrado na resposta");
                if (result.TryGetProperty("promptFeedback", out var feedback))
                {
                    Console.WriteLine($"[DEBUG] GeminiProvider: Prompt feedback encontrado");
                    if (feedback.TryGetProperty("blockReason", out var blockReason))
                    {
                        Console.WriteLine($"[ERROR] GeminiProvider: Prompt bloqueado - Reason: {blockReason.GetString()}");
                    }
                }
            }

            Console.WriteLine($"[DEBUG] GeminiProvider: Retornando null - nenhuma resposta válida encontrada");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"[ERROR] GeminiProvider: Timeout na requisição - {ex.Message}");
            Console.WriteLine($"[DEBUG] GeminiProvider: StackTrace: {ex.StackTrace}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[ERROR] GeminiProvider: Erro de requisição HTTP - {ex.Message}");
            if (ex.Data.Contains("StatusCode"))
            {
                Console.WriteLine($"[DEBUG] GeminiProvider: StatusCode: {ex.Data["StatusCode"]}");
            }
            Console.WriteLine($"[DEBUG] GeminiProvider: StackTrace: {ex.StackTrace}");
            return null;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[ERROR] GeminiProvider: Erro ao parsear JSON - {ex.Message}");
            Console.WriteLine($"[DEBUG] GeminiProvider: Path: {ex.Path}");
            Console.WriteLine($"[DEBUG] GeminiProvider: StackTrace: {ex.StackTrace}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GeminiProvider: Exceção não tratada - {ex.Message}");
            Console.WriteLine($"[DEBUG] GeminiProvider: Tipo: {ex.GetType().Name}");
            Console.WriteLine($"[DEBUG] GeminiProvider: StackTrace: {ex.StackTrace}");
            return null;
        }
    }
}


