using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlConnector;
using skybot.Models;

var builder = WebApplication.CreateBuilder(args);

// Adiciona HttpClient ao contêiner de serviços
builder.Services.AddHttpClient();

// Carrega configurações do appsettings.json
var configuration = builder.Configuration;

// Configuração de CORS - Completamente permissivo para desenvolvimento
builder.Services.AddCors(options =>
{
    options.AddPolicy("FinanGuardPolicy", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["liveness"])
    .AddMySql(builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string não configurada"));

var app = builder.Build();

// Endpoint para gerar o link de instalação
app.MapGet("/slack/install", () =>
{
    var clientId = configuration["Slack:ClientId"];
    var redirectUri = configuration["Slack:RedirectUri"];
    var scopes = configuration["Slack:Scopes"]; // Ex.: "chat:write,channels:read"

    var installUrl = $"https://slack.com/oauth/v2/authorize?client_id={clientId}&scope={scopes}&redirect_uri={redirectUri}";
    return Results.Ok(
        new
        {
            InstallUrl = installUrl
        });
});

// Endpoint para processar o callback OAuth
app.MapGet("/slack/oauth", async (string code, string? state, HttpClient httpClient) =>
{
    if (string.IsNullOrEmpty(code))
    {
        return Results.BadRequest("Código de autorização não fornecido.");
    }

    var clientId = configuration["Slack:ClientId"];
    var clientSecret = configuration["Slack:ClientSecret"];
    var redirectUri = configuration["Slack:RedirectUri"];

    var formData = new Dictionary<string, string>
    {
        { "client_id", clientId },
        { "client_secret", clientSecret },
        { "code", code },
        { "redirect_uri", redirectUri }
    };

    try
    {
        var content = new FormUrlEncodedContent(formData);
        var response = await httpClient.PostAsync("https://slack.com/api/oauth.v2.access", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        var oauthResponse = JsonSerializer.Deserialize<SlackOAuthResponse>(responseContent);

        if (!oauthResponse.Ok)
        {
            return Results.BadRequest($"Erro ao obter token: {oauthResponse.Error}");
        }

        // Armazena o token no MySQL
        await StoreTokenAsync(oauthResponse.AccessToken, oauthResponse.TeamId, oauthResponse.TeamName);

        return Results.Ok(new
        {
            Message = "App instalado com sucesso!",
            TeamId = oauthResponse.TeamId,
            TeamName = oauthResponse.TeamName
        });
    }
    catch (Exception ex)
    {
        return Results.StatusCode(500);
    }
});

// Health check endpoint
app.MapHealthChecks("/health/liveness", new()
{
    Predicate = check => check.Tags.Contains("liveness")
});

app.MapHealthChecks("/health", new ()
{
    Predicate = _ => true  // Mostra todos os checks
});

// Endpoint de informações da API
app.MapGet("/", () => new
{
    Application = "Skybot API",
    Version = "1.0.0",
    Environment = app.Environment.EnvironmentName,
    Timestamp = DateTime.UtcNow
});

app.Run();
return;

// Método para armazenar o token no MySQL
async Task StoreTokenAsync(string accessToken, string teamId, string teamName)
{
    var connectionString = configuration.GetConnectionString("MySqlConnection");
    await using var connection = new MySqlConnection(connectionString);

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