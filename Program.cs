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
    options.AddPolicy("SkybotPolicy", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["liveness"])
    .AddMySql(builder.Configuration.GetConnectionString("MySqlConnection") ?? throw new InvalidOperationException("Connection string não configurada"));

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

    Console.WriteLine("FormData: {0}", JsonSerializer.Serialize(formData));

    try
    {
        var content = new FormUrlEncodedContent(formData);
        var response = await httpClient.PostAsync("https://slack.com/api/oauth.v2.access", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        Console.WriteLine("=== RESPOSTA DO SLACK (oauth.v2.access) ===");
        Console.WriteLine(responseContent);
        Console.WriteLine("=== FIM DA RESPOSTA ===");

        var oauthResponse = JsonSerializer.Deserialize<SlackOAuthResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (!oauthResponse.Ok)
        {
            return Results.BadRequest($"Erro ao obter token: {oauthResponse.Error}");
        }

        // Armazena o token no MySQL
        await StoreTokenAsync(oauthResponse.AccessToken, oauthResponse.Team!.Id, oauthResponse.Team!.Name);

        return Results.Ok(new
        {
            Message = "App instalado com sucesso!",
            TeamId = oauthResponse.Team!.Id,
            TeamName = oauthResponse.Team!.Name
        });
    }
    catch (Exception ex)
    {
        return Results.StatusCode(500);
    }
});

// Eventos do Slack
app.MapPost("/slack/events", async (HttpRequest request, HttpClient httpClient) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    // Verificação de URL
    var urlVerification = JsonSerializer.Deserialize<SlackUrlVerification>(body, jsonOptions);
    if (urlVerification?.Type == "url_verification")
    {
        return Results.Ok(new { challenge = urlVerification.Challenge });
    }

    var eventWrapper = JsonSerializer.Deserialize<SlackEventWrapper>(body, jsonOptions);

    if (eventWrapper?.Event?.Type == "message" && !string.IsNullOrEmpty(eventWrapper.Event.Text))
    {
        var teamId = eventWrapper.TeamId;
        var eventData = eventWrapper.Event;

        if (eventData.User == "USLACKBOT" || eventData.Text.Contains("bot_id"))
        {
            return Results.Ok();
        }

        var token = await GetTokenAsync(teamId);
        if (token == null)
        {
            return Results.BadRequest("Token não encontrado para o workspace.");
        }

        var responseMessage = new
        {
            channel = eventData.Channel,
            text = $"Olá! Você disse: {eventData.Text}"
        };

        var content = new StringContent(JsonSerializer.Serialize(responseMessage), System.Text.Encoding.UTF8, "application/json");
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await httpClient.PostAsync("https://slack.com/api/chat.postMessage", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        Console.WriteLine("ResponseContent: {0}", responseContent);

        return Results.Ok();
    }

    return Results.Ok();
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

async Task<SlackToken> GetTokenAsync(string teamId)
{
    var connectionString = configuration.GetConnectionString("MySqlConnection");
    await using var connection = new MySqlConnection(connectionString);
    return await connection.QueryFirstOrDefaultAsync<SlackToken>(
        "SELECT * FROM SlackTokens WHERE TeamId = @TeamId", new { TeamId = teamId });
}