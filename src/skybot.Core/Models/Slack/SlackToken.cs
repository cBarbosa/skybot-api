namespace skybot.Core.Models;

public record SlackToken(int Id, string TeamId, string TeamName, string AccessToken, DateTime CreatedAt, DateTime UpdatedAt);

