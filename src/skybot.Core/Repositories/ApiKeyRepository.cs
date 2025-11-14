using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Microsoft.Extensions.Configuration;
using skybot.Core.Interfaces;
using skybot.Core.Models.Auth;

namespace skybot.Core.Repositories;

public class ApiKeyRepository : IApiKeyRepository
{
    private readonly string _connectionString;

    public ApiKeyRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySqlConnection") 
            ?? throw new InvalidOperationException("Connection string não configurada");
    }

    public async Task<ApiKey?> GetByKeyAsync(string apiKey)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var result = await connection.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM ApiKeys WHERE ApiKey = @ApiKey",
            new { ApiKey = apiKey });

        if (result == null)
            return null;

        return MapToApiKey(result);
    }

    public async Task<List<ApiKey>> GetByTeamIdAsync(string teamId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var results = await connection.QueryAsync<dynamic>(
            "SELECT * FROM ApiKeys WHERE TeamId = @TeamId ORDER BY CreatedAt DESC",
            new { TeamId = teamId });

        return results.Select(MapToApiKey).ToList();
    }

    public async Task<string> CreateAsync(string teamId, string name, List<string>? allowedEndpoints = null, DateTime? expiresAt = null)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        // Gera uma chave aleatória segura
        var apiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        
        var allowedEndpointsJson = allowedEndpoints != null && allowedEndpoints.Count > 0
            ? JsonSerializer.Serialize(allowedEndpoints)
            : null;

        await connection.ExecuteAsync(
            @"INSERT INTO ApiKeys (TeamId, ApiKey, Name, IsActive, AllowedEndpoints, ExpiresAt) 
              VALUES (@TeamId, @ApiKey, @Name, @IsActive, @AllowedEndpoints, @ExpiresAt)",
            new 
            { 
                TeamId = teamId, 
                ApiKey = apiKey, 
                Name = name, 
                IsActive = true,
                AllowedEndpoints = allowedEndpointsJson,
                ExpiresAt = expiresAt
            });

        return apiKey;
    }

    public async Task<bool> RevokeAsync(string apiKey)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var rowsAffected = await connection.ExecuteAsync(
            "UPDATE ApiKeys SET IsActive = FALSE WHERE ApiKey = @ApiKey",
            new { ApiKey = apiKey });

        return rowsAffected > 0;
    }

    public async Task UpdateLastUsedAsync(string apiKey)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        await connection.ExecuteAsync(
            "UPDATE ApiKeys SET LastUsedAt = CURRENT_TIMESTAMP WHERE ApiKey = @ApiKey",
            new { ApiKey = apiKey });
    }

    private static ApiKey MapToApiKey(dynamic result)
    {
        List<string>? allowedEndpoints = null;
        if (result.AllowedEndpoints != null)
        {
            var jsonString = result.AllowedEndpoints as string;
            if (!string.IsNullOrEmpty(jsonString))
            {
                allowedEndpoints = JsonSerializer.Deserialize<List<string>>(jsonString);
            }
        }

        return new ApiKey(
            Id: (int)result.Id,
            TeamId: (string)result.TeamId,
            Key: (string)result.ApiKey,
            Name: (string)result.Name,
            IsActive: (bool)result.IsActive,
            AllowedEndpoints: allowedEndpoints,
            CreatedAt: (DateTime)result.CreatedAt,
            LastUsedAt: result.LastUsedAt as DateTime?,
            ExpiresAt: result.ExpiresAt as DateTime?
        );
    }
}

