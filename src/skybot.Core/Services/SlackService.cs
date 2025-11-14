using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Http;
using skybot.Core.Interfaces;
using skybot.Core.Models;

namespace skybot.Core.Services;

public class SlackService : ISlackService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISlackTokenRepository _tokenRepository;
    private readonly ISlackTokenRefreshService? _tokenRefreshService;

    public SlackService(
        IHttpClientFactory httpClientFactory, 
        ISlackTokenRepository tokenRepository,
        ISlackTokenRefreshService? tokenRefreshService = null)
    {
        _httpClientFactory = httpClientFactory;
        _tokenRepository = tokenRepository;
        _tokenRefreshService = tokenRefreshService;
    }

    private async Task<string?> HandleTokenRefreshAsync(string teamId, string? error)
    {
        // Verifica se é erro de autenticação que requer refresh
        if (error != "invalid_auth" && error != "token_expired")
            return null;

        // Tenta renovar o token
        if (_tokenRefreshService != null && await _tokenRefreshService.RefreshTokenAsync(teamId))
        {
            // Busca o novo token
            var updatedToken = await _tokenRepository.GetTokenAsync(teamId);
            return updatedToken?.AccessToken;
        }

        return null;
    }

    public async Task<bool> SendMessageAsync(string token, string channel, string text, string? threadTs = null)
    {
        var client = _httpClientFactory.CreateClient();
        try
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var payload = new { channel, text, thread_ts = threadTs };
            var resp = await client.PostAsync("https://slack.com/api/chat.postMessage", JsonContent.Create(payload));
            var json = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(json);
            
            if (!result.GetProperty("ok").GetBoolean())
            {
                var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : "desconhecido";
                Console.WriteLine($"[WARNING] Falha ao enviar mensagem para canal {channel}: {error}");
                return false;
            }
            
            return true;
        }
        catch(Exception ex)
        {
            Console.WriteLine($"[ERROR] Exceção ao enviar mensagem: {ex.Message}");
            Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
            return false;
        }
    }

    public async Task<CreateChannelResult> CreateChannelAsync(string teamId, string name, HttpClient? httpClient = null)
    {
        var token = await _tokenRepository.GetTokenAsync(teamId);
        if (token == null)
            return new CreateChannelResult(false, "Token não encontrado para o team.");

        var normalizedName = name.Trim().ToLower().Replace(" ", "-");
        if (normalizedName.Length < 1 || normalizedName.Length > 80)
            return new CreateChannelResult(false, "Nome inválido (1-80 caracteres).");

        var client = httpClient ?? _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);

        var payload = new { name = normalizedName, is_private = false };
        var resp = await client.PostAsync("https://slack.com/api/conversations.create", JsonContent.Create(payload));
        var json = await resp.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);

        if (!result.GetProperty("ok").GetBoolean())
        {
            var error = result.GetProperty("error").GetString() ?? "desconhecido";
            
            // Tenta refresh se erro de autenticação
            var newToken = await HandleTokenRefreshAsync(teamId, error);
            if (newToken != null)
            {
                // Tenta novamente com o novo token
                client.DefaultRequestHeaders.Authorization = new("Bearer", newToken);
                resp = await client.PostAsync("https://slack.com/api/conversations.create", JsonContent.Create(payload));
                json = await resp.Content.ReadAsStringAsync();
                result = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (result.GetProperty("ok").GetBoolean())
                {
                    var newChannelId = result.GetProperty("channel").GetProperty("id").GetString();
                    return new CreateChannelResult(
                        Success: true,
                        Message: $"Criei o canal #{normalizedName}!",
                        ChannelId: newChannelId,
                        ChannelName: normalizedName
                    );
                }
            }
            
            return new CreateChannelResult(false, $"Erro do Slack: {error}");
        }

        var channelId = result.GetProperty("channel").GetProperty("id").GetString();
        return new CreateChannelResult(
            Success: true,
            Message: $"Criei o canal #{normalizedName}!",
            ChannelId: channelId,
            ChannelName: normalizedName
        );
    }

    public async Task<ListMembersResult> ListChannelMembersAsync(string teamId, string channelId, HttpClient? httpClient = null, int maxMembers = 10)
    {
        var token = await _tokenRepository.GetTokenAsync(teamId);
        if (token == null)
            return new ListMembersResult(false, "Token não encontrado para o team.");

        var client = httpClient ?? _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);

        // 1. Pega IDs dos membros
        var membersResp = await client.PostAsync(
            $"https://slack.com/api/conversations.members?channel={channelId}&limit=100", null);
        var membersJson = await membersResp.Content.ReadAsStringAsync();
        var membersResult = JsonSerializer.Deserialize<JsonElement>(membersJson);

        if (!membersResult.GetProperty("ok").GetBoolean())
        {
            var error = membersResult.GetProperty("error").GetString() ?? "desconhecido";
            
            // Tenta refresh se erro de autenticação
            var newToken = await HandleTokenRefreshAsync(teamId, error);
            if (newToken != null)
            {
                // Tenta novamente com o novo token
                client.DefaultRequestHeaders.Authorization = new("Bearer", newToken);
                membersResp = await client.PostAsync(
                    $"https://slack.com/api/conversations.members?channel={channelId}&limit=100", null);
                membersJson = await membersResp.Content.ReadAsStringAsync();
                membersResult = JsonSerializer.Deserialize<JsonElement>(membersJson);
                
                if (!membersResult.GetProperty("ok").GetBoolean())
                {
                    error = membersResult.GetProperty("error").GetString() ?? "desconhecido";
                    return new ListMembersResult(false, $"Erro ao listar membros: {error}");
                }
            }
            else
            {
                return new ListMembersResult(false, $"Erro ao listar membros: {error}");
            }
        }

        var memberIds = membersResult.GetProperty("members").EnumerateArray()
            .Select(m => m.GetString()!)
            .Take(maxMembers)
            .ToList();

        if (memberIds.Count == 0)
            return new ListMembersResult(true, "Nenhum membro encontrado.", Array.Empty<SlackUserInfo>());

        // 2. Pega info de cada usuário
        var members = new List<SlackUserInfo>();
        foreach (var userId in memberIds)
        {
            var infoResp = await client.PostAsync($"https://slack.com/api/users.info?user={userId}", null);
            var infoJson = await infoResp.Content.ReadAsStringAsync();
            var infoResult = JsonSerializer.Deserialize<JsonElement>(infoJson);

            if (infoResult.GetProperty("ok").GetBoolean())
            {
                var user = infoResult.GetProperty("user");
                var profile = user.GetProperty("profile");
                var display = profile.GetProperty("display_name").GetString()
                              ?? profile.GetProperty("real_name").GetString()
                              ?? user.GetProperty("name").GetString()
                              ?? "Usuário";

                members.Add(new SlackUserInfo(
                    UserId: userId,
                    DisplayName: display,
                    RealName: profile.TryGetProperty("real_name", out var rn) ? rn.GetString() : null,
                    Email: profile.TryGetProperty("email", out var em) ? em.GetString() : null
                ));
            }
        }

        var names = string.Join(", ", members.Select(m => m.DisplayName));
        return new ListMembersResult(true, $"Membros ({members.Count}): {names}", members);
    }

    // Descobre o TeamId a partir de um token usando auth.test
    public async Task<string?> GetTeamIdFromTokenAsync(string token, HttpClient? httpClient = null)
    {
        var client = httpClient ?? _httpClientFactory.CreateClient();
        try
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await client.PostAsync("https://slack.com/api/auth.test", null);
            var json = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(json);
            
            if (result.GetProperty("ok").GetBoolean())
            {
                return result.TryGetProperty("team_id", out var teamIdProp) 
                    ? teamIdProp.GetString() 
                    : null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Erro ao obter TeamId do token: {ex.Message}");
        }
        return null;
    }

    public async Task<bool> SendBlocksAsync(string token, string channel, object[] blocks, string? threadTs = null)
    {
        var client = _httpClientFactory.CreateClient();
        try
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var payload = new { channel, blocks, thread_ts = threadTs };
            var resp = await client.PostAsync("https://slack.com/api/chat.postMessage", JsonContent.Create(payload));
            var json = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(json);
            
            if (!result.GetProperty("ok").GetBoolean())
            {
                var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : "desconhecido";
                Console.WriteLine($"[WARNING] Falha ao enviar blocos para canal {channel}: {error}");
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Erro ao enviar blocos: {ex.Message}");
            Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
            return false;
        }
    }

    public async Task<Models.Slack.SendMessageResult> SendMessageAsync(string teamId, Models.Slack.SendMessageRequest request)
    {
        // Buscar token do repositório
        var tokenData = await _tokenRepository.GetTokenAsync(teamId);
        if (tokenData == null)
        {
            return new Models.Slack.SendMessageResult(
                Success: false,
                Error: "Token não encontrado para o workspace"
            );
        }

        var token = tokenData.AccessToken;
        var client = _httpClientFactory.CreateClient();

        // Determinar o destino baseado no tipo
        var destination = request.DestinationType switch
        {
            Models.Slack.DestinationType.CHANNEL => request.DestinationId,
            Models.Slack.DestinationType.USER => request.DestinationId,
            Models.Slack.DestinationType.GROUP => request.DestinationId,
            _ => request.DestinationId
        };

        // Construir payload - Text OU Blocks
        var payload = new Dictionary<string, object>
        {
            ["channel"] = destination
        };

        if (request.Text != null)
        {
            payload["text"] = request.Text;
        }
        else if (request.Blocks != null)
        {
            payload["blocks"] = request.Blocks;
        }

        if (request.ThreadTs != null)
        {
            payload["thread_ts"] = request.ThreadTs;
        }

        try
        {
            // Primeira tentativa
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await client.PostAsync(
                "https://slack.com/api/chat.postMessage",
                JsonContent.Create(payload)
            );

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(json);

            if (!result.GetProperty("ok").GetBoolean())
            {
                var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : "desconhecido";

                // Tentar refresh do token se for erro de autenticação
                var newToken = await HandleTokenRefreshAsync(teamId, error);
                if (newToken != null)
                {
                    // Retry com o novo token
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                    var retryResponse = await client.PostAsync(
                        "https://slack.com/api/chat.postMessage",
                        JsonContent.Create(payload)
                    );

                    var retryJson = await retryResponse.Content.ReadAsStringAsync();
                    var retryResult = JsonSerializer.Deserialize<JsonElement>(retryJson);

                    if (retryResult.GetProperty("ok").GetBoolean())
                    {
                        var messageTs = retryResult.TryGetProperty("ts", out var tsProp) ? tsProp.GetString() : null;
                        return new Models.Slack.SendMessageResult(
                            Success: true,
                            MessageTs: messageTs
                        );
                    }
                    else
                    {
                        var retryError = retryResult.TryGetProperty("error", out var retryErrProp) 
                            ? retryErrProp.GetString() 
                            : "desconhecido";
                        return new Models.Slack.SendMessageResult(
                            Success: false,
                            Error: retryError
                        );
                    }
                }

                return new Models.Slack.SendMessageResult(
                    Success: false,
                    Error: error
                );
            }

            var msgTs = result.TryGetProperty("ts", out var tsProperty) ? tsProperty.GetString() : null;
            return new Models.Slack.SendMessageResult(
                Success: true,
                MessageTs: msgTs
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Erro ao enviar mensagem: {ex.Message}");
            return new Models.Slack.SendMessageResult(
                Success: false,
                Error: $"Exceção: {ex.Message}"
            );
        }
    }

    public async Task<Models.Slack.DeleteMessageResult> DeleteMessageAsync(string teamId, string channel, string messageTs)
    {
        // Buscar token do repositório
        var tokenData = await _tokenRepository.GetTokenAsync(teamId);
        if (tokenData == null)
        {
            return new Models.Slack.DeleteMessageResult(
                Success: false,
                Error: "Token não encontrado para o workspace"
            );
        }

        var token = tokenData.AccessToken;
        var client = _httpClientFactory.CreateClient();

        var payload = new
        {
            channel = channel,
            ts = messageTs
        };

        try
        {
            // Primeira tentativa
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await client.PostAsync(
                "https://slack.com/api/chat.delete",
                JsonContent.Create(payload)
            );

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(json);

            if (!result.GetProperty("ok").GetBoolean())
            {
                var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : "desconhecido";

                // Tentar refresh do token se for erro de autenticação
                var newToken = await HandleTokenRefreshAsync(teamId, error);
                if (newToken != null)
                {
                    // Retry com o novo token
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                    var retryResponse = await client.PostAsync(
                        "https://slack.com/api/chat.delete",
                        JsonContent.Create(payload)
                    );

                    var retryJson = await retryResponse.Content.ReadAsStringAsync();
                    var retryResult = JsonSerializer.Deserialize<JsonElement>(retryJson);

                    if (retryResult.GetProperty("ok").GetBoolean())
                    {
                        return new Models.Slack.DeleteMessageResult(Success: true);
                    }
                    else
                    {
                        var retryError = retryResult.TryGetProperty("error", out var retryErrProp) 
                            ? retryErrProp.GetString() 
                            : "desconhecido";
                        return new Models.Slack.DeleteMessageResult(Success: false, Error: retryError);
                    }
                }

                return new Models.Slack.DeleteMessageResult(Success: false, Error: error);
            }

            return new Models.Slack.DeleteMessageResult(Success: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Erro ao apagar mensagem: {ex.Message}");
            return new Models.Slack.DeleteMessageResult(Success: false, Error: $"Exceção: {ex.Message}");
        }
    }
}

