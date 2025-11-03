// Modelo para a resposta do Slack OAuth
namespace skybot.Models;

internal record SlackOAuthResponse(
    bool Ok,
    string AccessToken,
    SlackTeam? Team,          // objeto aninhado
    string? Error = null);

internal record SlackTeam(string Id, string Name);