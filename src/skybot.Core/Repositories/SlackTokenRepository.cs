using Dapper;
using MySqlConnector;
using Microsoft.Extensions.Configuration;
using skybot.Core.Interfaces;
using skybot.Core.Models;

namespace skybot.Core.Repositories;

public class SlackTokenRepository : ISlackTokenRepository
{
    private readonly string _connectionString;

    public SlackTokenRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySqlConnection") 
            ?? throw new InvalidOperationException("Connection string não configurada");
    }

    public async Task StoreTokenAsync(string accessToken, string? refreshToken, string teamId, string teamName)
    {
        await using var connection = new MySqlConnection(_connectionString);

        // Verifica se o teamId já existe
        var existingToken = await connection.QueryFirstOrDefaultAsync<SlackToken>(
            "SELECT * FROM SlackTokens WHERE TeamId = @TeamId",
            new { TeamId = teamId });

        if (existingToken != null)
        {
            // Atualiza o token existente
            await connection.ExecuteAsync(
                "UPDATE SlackTokens SET AccessToken = @AccessToken, RefreshToken = @RefreshToken, TeamName = @TeamName, UpdatedAt = CURRENT_TIMESTAMP WHERE TeamId = @TeamId",
                new { TeamId = teamId, TeamName = teamName, AccessToken = accessToken, RefreshToken = refreshToken });
        }
        else
        {
            // Insere um novo token
            await connection.ExecuteAsync(
                "INSERT INTO SlackTokens (TeamId, TeamName, AccessToken, RefreshToken) VALUES (@TeamId, @TeamName, @AccessToken, @RefreshToken)",
                new { TeamId = teamId, TeamName = teamName, AccessToken = accessToken, RefreshToken = refreshToken });
        }
    }

    public async Task UpdateTokenAsync(string teamId, string accessToken, string? refreshToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.ExecuteAsync(
            "UPDATE SlackTokens SET AccessToken = @AccessToken, RefreshToken = @RefreshToken, UpdatedAt = CURRENT_TIMESTAMP WHERE TeamId = @TeamId",
            new { TeamId = teamId, AccessToken = accessToken, RefreshToken = refreshToken });
    }

    public async Task<SlackToken?> GetTokenAsync(string teamId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<SlackToken>(
            "SELECT * FROM SlackTokens WHERE TeamId = @TeamId", 
            new { TeamId = teamId });
    }

    public async Task DeleteTokenAsync(string teamId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.ExecuteAsync(
            "DELETE FROM SlackTokens WHERE TeamId = @TeamId", 
            new { TeamId = teamId });
    }

    public async Task<List<SlackToken>> GetAllTokensAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        var tokens = await connection.QueryAsync<SlackToken>(
            "SELECT * FROM SlackTokens ORDER BY UpdatedAt DESC");
        return tokens.ToList();
    }

    public async Task<Dictionary<string, SlackToken>> GetTokensByTeamIdsAsync(IEnumerable<string> teamIds)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var teamIdList = teamIds.Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
        if (teamIdList.Count == 0)
            return new Dictionary<string, SlackToken>();
        
        var tokens = await connection.QueryAsync<SlackToken>(
            "SELECT * FROM SlackTokens WHERE TeamId IN @TeamIds",
            new { TeamIds = teamIdList });
        
        return tokens.ToDictionary(t => t.TeamId, t => t);
    }
}

