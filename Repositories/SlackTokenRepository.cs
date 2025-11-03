using Dapper;
using MySqlConnector;
using skybot.Models;

namespace skybot.Repositories;

internal class SlackTokenRepository
{
    private readonly string _connectionString;

    public SlackTokenRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySqlConnection") 
            ?? throw new InvalidOperationException("Connection string não configurada");
    }

    public async Task StoreTokenAsync(string accessToken, string teamId, string teamName)
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
                "UPDATE SlackTokens SET AccessToken = @AccessToken, TeamName = @TeamName, UpdatedAt = CURRENT_TIMESTAMP WHERE TeamId = @TeamId",
                new { TeamId = teamId, TeamName = teamName, AccessToken = accessToken });
        }
        else
        {
            // Insere um novo token
            await connection.ExecuteAsync(
                "INSERT INTO SlackTokens (TeamId, TeamName, AccessToken) VALUES (@TeamId, @TeamName, @AccessToken)",
                new { TeamId = teamId, TeamName = teamName, AccessToken = accessToken });
        }
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
}

