using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using skybot.Core.Interfaces;
using skybot.Core.Models;

namespace skybot.Core.Services;

public interface ISlackTokenRefreshService
{
    Task<bool> RefreshTokenIfNeededAsync(string teamId);
    Task<bool> RefreshTokenAsync(string teamId);
    Task<bool> ShouldRefreshTokenAsync(string teamId);
}

public class SlackTokenRefreshService : ISlackTokenRefreshService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISlackTokenRepository _tokenRepository;
    private readonly ITokenRefreshHistoryRepository _historyRepository;
    private readonly IConfiguration _configuration;

    public SlackTokenRefreshService(
        IHttpClientFactory httpClientFactory,
        ISlackTokenRepository tokenRepository,
        ITokenRefreshHistoryRepository historyRepository,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _tokenRepository = tokenRepository;
        _historyRepository = historyRepository;
        _configuration = configuration;
    }

    public async Task<bool> ShouldRefreshTokenAsync(string teamId)
    {
        var token = await _tokenRepository.GetTokenAsync(teamId);
        if (token == null || string.IsNullOrEmpty(token.RefreshToken))
            return false;

        // Testa se o token atual ainda é válido
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        
        try
        {
            var testResponse = await client.PostAsync("https://slack.com/api/auth.test", null);
            var testJson = await testResponse.Content.ReadAsStringAsync();
            var testResult = JsonSerializer.Deserialize<JsonElement>(testJson);

            // Se o token ainda é válido, não precisa renovar
            if (testResult.GetProperty("ok").GetBoolean())
                return false;

            // Se o token expirou, precisa renovar
            var error = testResult.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
            return error == "invalid_auth" || error == "token_expired";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Erro ao verificar token para team {teamId}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RefreshTokenIfNeededAsync(string teamId)
    {
        var shouldRefresh = await ShouldRefreshTokenAsync(teamId);
        if (!shouldRefresh)
            return true; // Token ainda válido

        return await RefreshTokenAsync(teamId);
    }

    public async Task<bool> RefreshTokenAsync(string teamId)
    {
        var token = await _tokenRepository.GetTokenAsync(teamId);
        if (token == null || string.IsNullOrEmpty(token.RefreshToken))
        {
            Console.WriteLine($"[WARNING] Token ou RefreshToken não encontrado para team {teamId}");
            return false;
        }

        var clientId = _configuration["Slack:ClientId"];
        var clientSecret = _configuration["Slack:ClientSecret"];

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            Console.WriteLine($"[ERROR] ClientId ou ClientSecret não configurados");
            return false;
        }

        // Armazena últimos caracteres do token antigo para auditoria
        var oldAccessTokenSuffix = token.AccessToken.Length > 10 
            ? token.AccessToken.Substring(token.AccessToken.Length - 10) 
            : "***";

        var formData = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "refresh_token", token.RefreshToken },
            { "grant_type", "refresh_token" }
        };

        try
        {
            var client = _httpClientFactory.CreateClient();
            var content = new FormUrlEncodedContent(formData);
            var response = await client.PostAsync("https://slack.com/api/oauth.v2.access", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            var refreshResponse = JsonSerializer.Deserialize<SlackOAuthResponse>(
                responseContent, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (refreshResponse == null || !refreshResponse.Ok)
            {
                var errorMessage = refreshResponse?.Error ?? "Erro desconhecido";
                Console.WriteLine($"[ERROR] Falha ao renovar token para team {teamId}: {errorMessage}");
                
                // Registra falha no histórico
                await _historyRepository.AddHistoryAsync(
                    teamId, 
                    token.RefreshToken, 
                    false, 
                    errorMessage, 
                    oldAccessTokenSuffix, 
                    null);

                // Se o refresh token também expirou, loga para administrador
                if (errorMessage.Contains("invalid_grant") || errorMessage.Contains("expired") || errorMessage.Contains("invalid"))
                {
                    Console.WriteLine($"[CRITICAL] Refresh token expirado para team {teamId}. Reautenticação manual necessária.");
                }

                return false;
            }

            // Armazena últimos caracteres do novo token para auditoria
            var newAccessTokenSuffix = refreshResponse.AccessToken.Length > 10 
                ? refreshResponse.AccessToken.Substring(refreshResponse.AccessToken.Length - 10) 
                : "***";

            // Atualiza o token no banco
            await _tokenRepository.UpdateTokenAsync(
                teamId, 
                refreshResponse.AccessToken, 
                refreshResponse.RefreshToken);

            // Registra sucesso no histórico
            await _historyRepository.AddHistoryAsync(
                teamId, 
                token.RefreshToken, 
                true, 
                null, 
                oldAccessTokenSuffix, 
                newAccessTokenSuffix);

            Console.WriteLine($"[INFO] Token renovado com sucesso para team {teamId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exceção ao renovar token para team {teamId}: {ex.Message}");
            Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
            
            // Registra exceção no histórico
            await _historyRepository.AddHistoryAsync(
                teamId, 
                token.RefreshToken, 
                false, 
                $"Exceção: {ex.Message}", 
                oldAccessTokenSuffix, 
                null);

            return false;
        }
    }
}

