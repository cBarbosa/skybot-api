// Modelo para a resposta do Slack OAuth
namespace skybot.Models;

internal record SlackOAuthResponse(bool Ok, string AccessToken, string TeamId, string TeamName, string Error);