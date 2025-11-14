using Dapper;
using MySqlConnector;
using Microsoft.Extensions.Configuration;
using skybot.Core.Interfaces;
using skybot.Core.Models;

namespace skybot.Core.Repositories;

public class TokenRefreshHistoryRepository : ITokenRefreshHistoryRepository
{
    private readonly string _connectionString;

    public TokenRefreshHistoryRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySqlConnection") 
            ?? throw new InvalidOperationException("Connection string não configurada");
    }

    public async Task AddHistoryAsync(string teamId, string refreshToken, bool success, string? errorMessage, string? oldAccessToken, string? newAccessToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.ExecuteAsync(
            @"INSERT INTO SlackTokenRefreshHistory (TeamId, RefreshToken, Success, ErrorMessage, OldAccessToken, NewAccessToken) 
              VALUES (@TeamId, @RefreshToken, @Success, @ErrorMessage, @OldAccessToken, @NewAccessToken)",
            new 
            { 
                TeamId = teamId, 
                RefreshToken = refreshToken, 
                Success = success, 
                ErrorMessage = errorMessage,
                OldAccessToken = oldAccessToken,
                NewAccessToken = newAccessToken
            });
    }

    public async Task<List<TokenRefreshHistory>> GetHistoryByTeamIdAsync(string teamId, int limit = 50)
    {
        await using var connection = new MySqlConnection(_connectionString);
        var history = await connection.QueryAsync<TokenRefreshHistory>(
            "SELECT * FROM SlackTokenRefreshHistory WHERE TeamId = @TeamId ORDER BY RefreshedAt DESC LIMIT @Limit",
            new { TeamId = teamId, Limit = limit });
        return history.ToList();
    }

    public async Task<List<TokenRefreshHistory>> GetRecentFailuresAsync(int hours = 24)
    {
        await using var connection = new MySqlConnection(_connectionString);
        var history = await connection.QueryAsync<TokenRefreshHistory>(
            @"SELECT * FROM SlackTokenRefreshHistory 
              WHERE Success = false AND RefreshedAt >= DATE_SUB(NOW(), INTERVAL @Hours HOUR)
              ORDER BY RefreshedAt DESC",
            new { Hours = hours });
        return history.ToList();
    }
}

