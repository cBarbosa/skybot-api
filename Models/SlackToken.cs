// Modelo para a entidade SlackToken

namespace skybot.Models;

internal record SlackToken(int Id, string TeamId, string TeamName, string AccessToken, DateTime CreatedAt, DateTime UpdatedAt);