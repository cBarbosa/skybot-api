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
        return await GetResponseAsync(userMessage, systemPrompt, null);
    }

    public async Task<string?> GetResponseAsync(string userMessage, string systemPrompt, List<(string Role, string Content)>? conversationHistory)
    {
        if (!IsConfigured)
        {
            Console.WriteLine("[DEBUG] OpenAIProvider: Não configurado (ApiKey ausente)");
            return null;
        }

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            // Adiciona histórico de conversa se existir
            if (conversationHistory != null && conversationHistory.Count > 0)
            {
                Console.WriteLine($"[DEBUG] OpenAIProvider: Adicionando {conversationHistory.Count} mensagens do histórico");
                foreach (var (role, content) in conversationHistory)
                {
                    messages.Add(new { role = role, content = content });
                }
            }
            else
            {
                Console.WriteLine("[DEBUG] OpenAIProvider: Sem histórico de conversa");
            }

            // Adiciona a mensagem atual do usuário
            messages.Add(new { role = "user", content = userMessage });

            var payload = new
            {
                model = _model,
                messages = messages,
                max_tokens = 500,
                temperature = 0.7
            };

            Console.WriteLine($"[DEBUG] OpenAIProvider: Enviando requisição para modelo {_model}");
            Console.WriteLine($"[DEBUG] OpenAIProvider: URL: https://api.openai.com/v1/chat/completions");
            Console.WriteLine($"[DEBUG] OpenAIProvider: User message length: {userMessage.Length} caracteres");
            Console.WriteLine($"[DEBUG] OpenAIProvider: System prompt length: {systemPrompt.Length} caracteres");
            Console.WriteLine($"[DEBUG] OpenAIProvider: Total messages: {messages.Count}");
            Console.WriteLine($"[DEBUG] OpenAIProvider: Max tokens: 500, Temperature: 0.7");

            var response = await _httpClient.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                JsonContent.Create(payload));

            var json = await response.Content.ReadAsStringAsync();
            
            Console.WriteLine($"[DEBUG] OpenAIProvider: Status Code: {response.StatusCode}");
            Console.WriteLine($"[DEBUG] OpenAIProvider: Response length: {json.Length} caracteres");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ERROR] OpenAIProvider: API error - Status: {response.StatusCode}");
                Console.WriteLine($"[ERROR] OpenAIProvider: Response body: {json}");
                
                try
                {
                    var errorJson = JsonSerializer.Deserialize<JsonElement>(json);
                    if (errorJson.TryGetProperty("error", out var error))
                    {
                        var message = error.TryGetProperty("message", out var msg) ? msg.GetString() : "N/A";
                        var type = error.TryGetProperty("type", out var errType) ? errType.GetString() : "N/A";
                        var code = error.TryGetProperty("code", out var errCode) ? errCode.GetString() : "N/A";
                        Console.WriteLine($"[DEBUG] OpenAIProvider: Error type: {type}, code: {code}, message: {message}");
                        
                        if (error.TryGetProperty("param", out var param))
                        {
                            Console.WriteLine($"[DEBUG] OpenAIProvider: Error param: {param.GetString()}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG] OpenAIProvider: Erro ao parsear resposta de erro: {ex.Message}");
                }
                
                return null;
            }

            var result = JsonSerializer.Deserialize<JsonElement>(json);
            
            Console.WriteLine($"[DEBUG] OpenAIProvider: Resposta parseada com sucesso");
            
            if (result.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                Console.WriteLine($"[DEBUG] OpenAIProvider: Número de choices: {choices.GetArrayLength()}");
                
                var choice = choices[0];
                
                // Verifica finish_reason
                if (choice.TryGetProperty("finish_reason", out var finishReason))
                {
                    var reason = finishReason.GetString();
                    Console.WriteLine($"[DEBUG] OpenAIProvider: Finish reason: {reason}");
                    
                    if (reason != "stop")
                    {
                        Console.WriteLine($"[WARNING] OpenAIProvider: Finish reason não é 'stop': {reason}");
                    }
                }
                
                // Verifica usage (tokens)
                if (result.TryGetProperty("usage", out var usage))
                {
                    var promptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                    var completionTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
                    var totalTokens = usage.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : 0;
                    Console.WriteLine($"[DEBUG] OpenAIProvider: Tokens - Prompt: {promptTokens}, Completion: {completionTokens}, Total: {totalTokens}");
                }
                
                if (choice.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("content", out var content))
                    {
                        var text = content.GetString()?.Trim();
                        Console.WriteLine($"[DEBUG] OpenAIProvider: Texto extraído, length: {text?.Length ?? 0} caracteres");
                        
                        // Verifica role da mensagem
                        if (message.TryGetProperty("role", out var role))
                        {
                            Console.WriteLine($"[DEBUG] OpenAIProvider: Message role: {role.GetString()}");
                        }
                        
                        return text;
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] OpenAIProvider: Property 'content' não encontrada na mensagem");
                    }
                }
                else
                {
                    Console.WriteLine($"[DEBUG] OpenAIProvider: Property 'message' não encontrada no choice");
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] OpenAIProvider: Nenhum choice encontrado na resposta");
                
                // Verifica se há erro na resposta mesmo com status 200
                if (result.TryGetProperty("error", out var error))
                {
                    Console.WriteLine($"[ERROR] OpenAIProvider: Erro encontrado na resposta: {error}");
                }
            }

            Console.WriteLine($"[DEBUG] OpenAIProvider: Retornando null - nenhuma resposta válida encontrada");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"[ERROR] OpenAIProvider: Timeout na requisição - {ex.Message}");
            Console.WriteLine($"[DEBUG] OpenAIProvider: StackTrace: {ex.StackTrace}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[ERROR] OpenAIProvider: Erro de requisição HTTP - {ex.Message}");
            if (ex.Data.Contains("StatusCode"))
            {
                Console.WriteLine($"[DEBUG] OpenAIProvider: StatusCode: {ex.Data["StatusCode"]}");
            }
            Console.WriteLine($"[DEBUG] OpenAIProvider: StackTrace: {ex.StackTrace}");
            return null;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[ERROR] OpenAIProvider: Erro ao parsear JSON - {ex.Message}");
            Console.WriteLine($"[DEBUG] OpenAIProvider: Path: {ex.Path}");
            Console.WriteLine($"[DEBUG] OpenAIProvider: StackTrace: {ex.StackTrace}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] OpenAIProvider: Exceção não tratada - {ex.Message}");
            Console.WriteLine($"[DEBUG] OpenAIProvider: Tipo: {ex.GetType().Name}");
            Console.WriteLine($"[DEBUG] OpenAIProvider: StackTrace: {ex.StackTrace}");
            return null;
        }
    }
}


