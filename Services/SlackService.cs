using System.Net.Http.Headers;
using System.Text.Json;
using skybot.Models;
using skybot.Repositories;

namespace skybot.Services;

internal class SlackService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SlackTokenRepository _tokenRepository;

    public SlackService(IHttpClientFactory httpClientFactory, SlackTokenRepository tokenRepository)
    {
        _httpClientFactory = httpClientFactory;
        _tokenRepository = tokenRepository;
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
            return new ListMembersResult(false, $"Erro ao listar membros: {error}");
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
}

