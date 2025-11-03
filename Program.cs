using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using skybot.Models;
using skybot.Repositories;
using skybot.Services;

var builder = WebApplication.CreateBuilder(args);

// Adiciona HttpClient ao contêiner de serviços
builder.Services.AddHttpClient();

// Carrega configurações do appsettings.json
var configuration = builder.Configuration;

// Registra repositórios e serviços
builder.Services.AddSingleton<SlackTokenRepository>();
builder.Services.AddSingleton<ReminderRepository>();
builder.Services.AddScoped<SlackService>();
builder.Services.AddHostedService<ReminderBackgroundService>();

// Valida configurações obrigatórias
var requiredConfigs = new[]
{
    ("Slack:ClientId", configuration["Slack:ClientId"]),
    ("Slack:ClientSecret", configuration["Slack:ClientSecret"]),
    ("Slack:RedirectUri", configuration["Slack:RedirectUri"]),
    ("Slack:Scopes", configuration["Slack:Scopes"]),
    ("ConnectionStrings:MySqlConnection", configuration.GetConnectionString("MySqlConnection"))
};

var missingConfigs = requiredConfigs.Where(c => string.IsNullOrWhiteSpace(c.Item2)).ToList();
if (missingConfigs.Any())
{
    throw new InvalidOperationException(
        $"Configurações obrigatórias não encontradas: {string.Join(", ", missingConfigs.Select(c => c.Item1))}");
}

// Configuração de CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("SkybotPolicy", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Desenvolvimento: permissivo
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // Produção: restrito
            // TODO: Configurar origens específicas em produção
            policy.WithOrigins("https://slack.com", "https://hooks.slack.com")
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["liveness"])
    .AddMySql(builder.Configuration.GetConnectionString("MySqlConnection") ?? throw new InvalidOperationException("Connection string não configurada"));

var app = builder.Build();

// Cache para evitar processar eventos duplicados do Slack
// Usa ConcurrentDictionary para thread-safety
var processedEvents = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>();
var cleanupInterval = TimeSpan.FromHours(1);

// Limpa eventos antigos periodicamente para evitar vazamento de memória
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(cleanupInterval);
        var cutoff = DateTime.UtcNow.Subtract(cleanupInterval);
        var keysToRemove = processedEvents
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in keysToRemove)
        {
            processedEvents.TryRemove(key, out _);
        }
        
        if (keysToRemove.Count > 0)
        {
            Console.WriteLine($"[INFO] Limpeza de cache: removidos {keysToRemove.Count} eventos antigos");
        }
    }
});

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
app.MapGet("/slack/oauth", async (string code, string? state, HttpClient httpClient, SlackTokenRepository tokenRepository) =>
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

        var oauthResponse = JsonSerializer.Deserialize<SlackOAuthResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (!oauthResponse.Ok)
        {
            return Results.BadRequest($"Erro ao obter token: {oauthResponse.Error}");
        }

        // Armazena o token no MySQL
        await tokenRepository.StoreTokenAsync(oauthResponse.AccessToken, oauthResponse.Team!.Id, oauthResponse.Team!.Name);

        return Results.Ok(new
        {
            Message = "App instalado com sucesso!",
            TeamId = oauthResponse.Team!.Id,
            TeamName = oauthResponse.Team!.Name
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] OAuth callback failed: {ex.Message}");
        Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
        return Results.StatusCode(500);
    }
});

// Lista estática de comandos (sem funções)
var helpCommands = new[]
{
    new { Name = "!ajuda", Description = "Mostra esta ajuda" },
    new { Name = "!ping", Description = "Responde pong!" },
    new { Name = "!horario", Description = "Mostra a hora atual" },
    new { Name = "!canal <nome>", Description = "Cria um canal público" },
    new { Name = "!membros", Description = "Lista membros do canal" }
};

var commands = new Dictionary<string, Func<SlackEvent, string, HttpClient, SlackService, Task>>(
    StringComparer.OrdinalIgnoreCase)
{
    ["!ajuda"] = async (evt, _, slackClient, slackService) =>
    {
        var help = string.Join("\n", helpCommands.Select(c => $"{c.Name} – {c.Description}"));
        await slackService.SendMessageAsync(evt.AccessToken, evt.Channel, help, evt.Ts);
    },

    ["!ping"] = async (evt, _, slackClient, slackService) =>
    {
        await slackService.SendMessageAsync(evt.AccessToken, evt.Channel, "pong!", evt.Ts);
    },

    ["!horario"] = async (evt, _, slackClient, slackService) =>
    {
        var now = TimezoneHelper.GetBrazilianTime();
        await slackService.SendMessageAsync(evt.AccessToken, evt.Channel, 
            $"Agora são {now:HH:mm} (horário de Brasília - UTC-3)", evt.Ts);
    },

    ["!canal"] = async (evt, args, slackClient, slackService) =>
    {
        if (string.IsNullOrEmpty(evt.TeamId))
        {
            await slackService.SendMessageAsync(evt.AccessToken, evt.Channel, "Erro: Team ID não encontrado.", evt.Ts);
            return;
        }
        var result = await slackService.CreateChannelAsync(evt.TeamId, args, slackClient);
        await slackService.SendMessageAsync(evt.AccessToken, evt.Channel, result.Message, evt.Ts);
    },

    ["!membros"] = async (evt, _, slackClient, slackService) =>
    {
        if (string.IsNullOrEmpty(evt.TeamId))
        {
            await slackService.SendMessageAsync(evt.AccessToken, evt.Channel, "Erro: Team ID não encontrado.", evt.Ts);
            return;
        }
        var result = await slackService.ListChannelMembersAsync(evt.TeamId, evt.Channel, slackClient, maxMembers: 10);
        await slackService.SendMessageAsync(evt.AccessToken, evt.Channel, result.Message, evt.Ts);
    }
};

app.MapPost("/slack/events", async (HttpRequest request, HttpClient slackClient, SlackService slackService, SlackTokenRepository tokenRepository) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    // URL Verification
    if (JsonSerializer.Deserialize<SlackUrlVerification>(body, jsonOptions) is { Type: "url_verification" } v)
        return Results.Ok(new { challenge = v.Challenge });

    var eventWrapper = JsonSerializer.Deserialize<SlackEventWrapper>(body, jsonOptions);
    var evt = eventWrapper?.Event;

    // Verifica se o evento já foi processado (deduplicação)
    if (!string.IsNullOrEmpty(eventWrapper?.EventId))
    {
        if (processedEvents.ContainsKey(eventWrapper.EventId))
        {
            Console.WriteLine($"[INFO] Evento duplicado ignorado: {eventWrapper.EventId}");
            return Results.Ok(); // Responde OK mesmo para eventos duplicados
        }
        // Adiciona o evento ao cache com timestamp atual
        processedEvents.TryAdd(eventWrapper.EventId, DateTime.UtcNow);
    }

    if (evt == null || evt.Subtype == "bot_message" || evt.BotId != null || string.IsNullOrEmpty(evt.User) || evt.Type != "message" || string.IsNullOrEmpty(evt.Text) || !evt.Text.Trim().StartsWith("!"))
        return Results.Ok();

    var teamId = eventWrapper.TeamId;
    var token = await tokenRepository.GetTokenAsync(teamId);
    if (token == null) return Results.Ok();

    var text = evt.Text.Trim();
    var spaceIndex = text.IndexOf(' ');
    var commandKey = spaceIndex > 0 ? text[..spaceIndex] : text;
    var args = spaceIndex > 0 ? text[(spaceIndex + 1)..] : "";

    if (!commands.TryGetValue(commandKey, out var action))
        return Results.Ok();

    // Passa slackService e slackClient para o comando, incluindo TeamId no evento
    await action(evt with { AccessToken = token.AccessToken, TeamId = teamId }, args, slackClient, slackService);

    return Results.Ok();
});

app.MapGet("/slack/channels", async (string teamId, HttpClient httpClient, SlackTokenRepository tokenRepository) =>
{
    var token = await tokenRepository.GetTokenAsync(teamId);
    if (token == null) return Results.BadRequest("Token não encontrado.");

    var formData = new Dictionary<string, string>
    {
        { "limit", "50" },
        { "types", "public_channel" },
        { "exclude_archived", "true" }
    };

    httpClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);

    var content = new FormUrlEncodedContent(formData);
    var response = await httpClient.PostAsync("https://slack.com/api/conversations.list", content);
    var json = await response.Content.ReadAsStringAsync();

    return Results.Ok(JsonSerializer.Deserialize<object>(json));
});

app.MapPost("/slack/channels", async (CreateChannelRequest req, HttpContext ctx, IHttpClientFactory httpClientFactory, SlackService slackService) =>
{
    var teamId = ctx.Request.Headers["X-Team-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(teamId))
        return Results.BadRequest(new CreateChannelResult(false, "Team ID não fornecido."));

    var httpClient = httpClientFactory.CreateClient();
    var result = await slackService.CreateChannelAsync(teamId, req.Name, httpClient);
    
    return result.Success 
        ? Results.Ok(result) 
        : Results.BadRequest(result);
});

app.MapGet("/slack/channels/{channelId}/members", async (string channelId, HttpContext ctx, IHttpClientFactory httpClientFactory, SlackService slackService) =>
{
    var teamId = ctx.Request.Headers["X-Team-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(teamId))
        return Results.BadRequest(new ListMembersResult(false, "Team ID não fornecido."));

    var httpClient = httpClientFactory.CreateClient();
    var result = await slackService.ListChannelMembersAsync(teamId, channelId, httpClient, maxMembers: 50);
    
    return result.Success 
        ? Results.Ok(result) 
        : Results.BadRequest(result);
});

app.MapGet("/slack/users", async (string teamId, HttpClient httpClient, SlackTokenRepository tokenRepository) =>
{
    var token = await tokenRepository.GetTokenAsync(teamId);
    if (token == null) return Results.BadRequest("Token não encontrado.");

    httpClient.DefaultRequestHeaders.Authorization = 
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);

    var response = await httpClient.PostAsync("https://slack.com/api/users.list", null);
    var json = await response.Content.ReadAsStringAsync();

    return Results.Content(json, "application/json");
});

app.MapDelete("/slack/team/{teamId}", async (string teamId, HttpClient httpClient, SlackTokenRepository tokenRepository) =>
{
    var token = await tokenRepository.GetTokenAsync(teamId);
    if (token == null) return Results.BadRequest("Token não encontrado.");

    // Revoga o token via auth.revoke
    httpClient.DefaultRequestHeaders.Authorization = 
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);

    var response = await httpClient.PostAsync("https://slack.com/api/auth.revoke", null);
    var json = await response.Content.ReadAsStringAsync();

    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var revokeResponse = JsonSerializer.Deserialize<JsonElement>(json, options);

    if (revokeResponse.GetProperty("ok").GetBoolean())
    {
        // Deleta do MySQL
        await tokenRepository.DeleteTokenAsync(teamId);
        return Results.Ok(new { Message = "Team desconectado com sucesso!", Revoked = true });
    }

    return Results.BadRequest($"Erro na revogação: {json}");
});

app.MapPost("/slack/home", async (string teamId, string userId, HttpClient http, SlackTokenRepository tokenRepository) =>
{
    var token = await tokenRepository.GetTokenAsync(teamId);
    if (token == null) return Results.BadRequest("Token não encontrado.");

    var home = new
    {
        user_id = userId,
        view = new
        {
            type = "home",
            blocks = new object[]
            {
                new { type = "section", text = new { type = "mrkdwn", text = "*Bem-vindo ao Bot!*" } },
                new { type = "actions", elements = new[] { new { type = "button", text = new { type = "plain_text", text = "Abrir Modal" }, action_id = "open_modal" } } }
            }
        }
    };

    http.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
    var resp = await http.PostAsync("https://slack.com/api/views.publish", JsonContent.Create(home));
    return Results.Content(await resp.Content.ReadAsStringAsync(), "application/json");
});

app.MapPost("/slack/join/{channelId}", async (string teamId, string channelId, HttpClient http, SlackTokenRepository tokenRepository) =>
{
    var token = await tokenRepository.GetTokenAsync(teamId);
    if (token == null) return Results.BadRequest("Token não encontrado.");

    http.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);

    var resp = await http.PostAsync($"https://slack.com/api/conversations.join?channel={channelId}", null);
    return Results.Content(await resp.Content.ReadAsStringAsync(), "application/json");
});

// Endpoint para criar lembrete
app.MapPost("/slack/reminders", async (
    CreateReminderRequest req, 
    HttpContext ctx,
    ReminderRepository reminderRepository) =>
{
    var teamId = ctx.Request.Headers["X-Team-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(teamId))
        return Results.BadRequest(new CreateReminderResult(false, "Team ID não fornecido no header X-Team-Id."));

    // Validações básicas
    if (string.IsNullOrWhiteSpace(req.UserId))
        return Results.BadRequest(new CreateReminderResult(false, "UserId é obrigatório."));

    if (string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new CreateReminderResult(false, "Message é obrigatória."));

    // Assume que req.DueDate vem em horário de Brasília (UTC-3)
    // Converte para UTC antes de salvar
    var dueDateUtc = TimezoneHelper.ConvertToUtc(req.DueDate);
    var utcNow = DateTime.UtcNow;
    
    // Compara em UTC para validação (ambos em UTC)
    if (dueDateUtc <= utcNow)
        return Results.BadRequest(new CreateReminderResult(false, "DueDate deve ser uma data futura (horário de Brasília)."));

    try
    {
        // Salva em UTC no banco
        var reminderId = await reminderRepository.CreateReminderAsync(
            teamId, 
            req.UserId, 
            req.Message, 
            dueDateUtc,  // Salva em UTC
            req.ChannelId);

        Console.WriteLine($"[INFO] Lembrete criado: Id={reminderId}, TeamId={teamId}, UserId={req.UserId}, DueDate (BR)={req.DueDate:dd/MM/yyyy HH:mm}");

        return Results.Ok(new CreateReminderResult(
            Success: true,
            Message: $"Lembrete criado com sucesso! Data (Brasília): {req.DueDate:dd/MM/yyyy HH:mm}",
            ReminderId: reminderId
        ));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Erro ao criar lembrete: {ex.Message}");
        Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
        return Results.BadRequest(new CreateReminderResult(false, $"Erro ao criar lembrete: {ex.Message}"));
    }
});

// Endpoint para listar lembretes do team
app.MapGet("/slack/reminders", async (
    HttpContext ctx,
    ReminderRepository reminderRepository) =>
{
    var teamId = ctx.Request.Headers["X-Team-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(teamId))
        return Results.BadRequest(new { Message = "Team ID não fornecido no header X-Team-Id." });

    try
    {
        var reminders = await reminderRepository.GetRemindersByTeamAsync(teamId);
        return Results.Ok(new
        {
            Count = reminders.Count,
            Reminders = reminders
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Erro ao buscar lembretes: {ex.Message}");
        return Results.StatusCode(500);
    }
});

// Endpoint para listar lembretes de um usuário específico
app.MapGet("/slack/reminders/user/{userId}", async (
    string userId,
    HttpContext ctx,
    ReminderRepository reminderRepository) =>
{
    var teamId = ctx.Request.Headers["X-Team-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(teamId))
        return Results.BadRequest(new { Message = "Team ID não fornecido no header X-Team-Id." });

    try
    {
        var reminders = await reminderRepository.GetRemindersByUserAsync(teamId, userId);
        return Results.Ok(new
        {
            Count = reminders.Count,
            Reminders = reminders
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Erro ao buscar lembretes do usuário: {ex.Message}");
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