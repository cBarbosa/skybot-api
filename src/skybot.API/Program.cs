using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using skybot.API.Extensions;
using skybot.Core.Interfaces;
using skybot.Core.Models;
using skybot.Core.Services;
using skybot.Core.Services.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;

// Configura serviços do Core e da API
builder.Services.AddApiServices(configuration);
builder.Services.AddApiCors(builder.Environment);
builder.Services.AddApiHealthChecks(configuration);
builder.Services.AddApiSwagger();

var app = builder.Build();

// Swagger apenas em desenvolvimento
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Skybot API v1");
        c.RoutePrefix = string.Empty; // Swagger na raiz
    });
}

app.UseCors("SkybotPolicy");
app.UseMiddleware<skybot.API.Middleware.ExceptionHandlingMiddleware>();

// Endpoint para gerar o link de instalação
app.MapGet("/slack/install", (IConfiguration config) =>
{
    var clientId = config["Slack:ClientId"];
    var redirectUri = config["Slack:RedirectUri"];
    var scopes = config["Slack:Scopes"];

    var installUrl = $"https://slack.com/oauth/v2/authorize?client_id={clientId}&scope={scopes}&redirect_uri={redirectUri}";
    return Results.Ok(new { InstallUrl = installUrl });
});

// Endpoint para processar o callback OAuth
app.MapGet("/slack/oauth", async (string code, string? state, HttpClient httpClient, ISlackTokenRepository tokenRepository, IConfiguration config) =>
{
    if (string.IsNullOrEmpty(code))
    {
        return Results.BadRequest("Código de autorização não fornecido.");
    }

    var clientId = config["Slack:ClientId"];
    var clientSecret = config["Slack:ClientSecret"];
    var redirectUri = config["Slack:RedirectUri"];

    var formData = new Dictionary<string, string>
    {
        { "client_id", clientId ?? "" },
        { "client_secret", clientSecret ?? "" },
        { "code", code },
        { "redirect_uri", redirectUri ?? "" }
    };

    try
    {
        var content = new FormUrlEncodedContent(formData);
        var response = await httpClient.PostAsync("https://slack.com/api/oauth.v2.access", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        var oauthResponse = JsonSerializer.Deserialize<SlackOAuthResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (oauthResponse == null || !oauthResponse.Ok)
        {
            return Results.BadRequest($"Erro ao obter token: {oauthResponse?.Error ?? "Erro desconhecido"}");
        }

        // Armazena o token e refresh token no MySQL
        await tokenRepository.StoreTokenAsync(oauthResponse.AccessToken, oauthResponse.RefreshToken, oauthResponse.Team!.Id, oauthResponse.Team!.Name);

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

// Endpoint para processar eventos do Slack
app.MapPost("/slack/events", async (HttpRequest request, ISlackIntegrationService slackIntegrationService) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    // URL Verification
    if (JsonSerializer.Deserialize<SlackUrlVerification>(body, jsonOptions) is { Type: "url_verification" } v)
        return Results.Ok(new { challenge = v.Challenge });

    var eventWrapper = JsonSerializer.Deserialize<SlackEventWrapper>(body, jsonOptions);
    if (eventWrapper == null)
        return Results.Ok();

    await slackIntegrationService.ProcessSlackEventAsync(eventWrapper);
    return Results.Ok();
});

// Endpoint para listar canais
app.MapGet("/slack/channels", async (string teamId, HttpClient httpClient, ISlackTokenRepository tokenRepository, ISlackTokenRefreshService? tokenRefreshService) =>
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
        new AuthenticationHeaderValue("Bearer", token.AccessToken);

    var content = new FormUrlEncodedContent(formData);
    var response = await httpClient.PostAsync("https://slack.com/api/conversations.list", content);
    var json = await response.Content.ReadAsStringAsync();
    var result = JsonSerializer.Deserialize<JsonElement>(json);

    // Se erro de autenticação, tenta refresh
    if (!result.GetProperty("ok").GetBoolean())
    {
        var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
        if ((error == "invalid_auth" || error == "token_expired") && tokenRefreshService != null)
        {
            if (await tokenRefreshService.RefreshTokenAsync(teamId))
            {
                token = await tokenRepository.GetTokenAsync(teamId);
                if (token != null)
                {
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                    response = await httpClient.PostAsync("https://slack.com/api/conversations.list", content);
                    json = await response.Content.ReadAsStringAsync();
                }
            }
        }
    }

    return Results.Ok(JsonSerializer.Deserialize<object>(json));
});

// Endpoint para criar canal
app.MapPost("/slack/channels", async (CreateChannelRequest req, HttpContext ctx, ISlackService slackService) =>
{
    var teamId = ctx.Request.Headers["X-Team-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(teamId))
        return Results.BadRequest(new CreateChannelResult(false, "Team ID não fornecido."));

    var result = await slackService.CreateChannelAsync(teamId, req.Name);
    
    return result.Success 
        ? Results.Ok(result) 
        : Results.BadRequest(result);
});

// Endpoint para listar membros do canal
app.MapGet("/slack/channels/{channelId}/members", async (string channelId, HttpContext ctx, ISlackService slackService) =>
{
    var teamId = ctx.Request.Headers["X-Team-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(teamId))
        return Results.BadRequest(new ListMembersResult(false, "Team ID não fornecido."));

    var result = await slackService.ListChannelMembersAsync(teamId, channelId, maxMembers: 50);
    
    return result.Success 
        ? Results.Ok(result) 
        : Results.BadRequest(result);
});

// Endpoint para listar usuários
app.MapGet("/slack/users", async (string teamId, HttpClient httpClient, ISlackTokenRepository tokenRepository, ISlackTokenRefreshService? tokenRefreshService) =>
{
    var token = await tokenRepository.GetTokenAsync(teamId);
    if (token == null) return Results.BadRequest("Token não encontrado.");

    httpClient.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", token.AccessToken);

    var response = await httpClient.PostAsync("https://slack.com/api/users.list", null);
    var json = await response.Content.ReadAsStringAsync();
    var result = JsonSerializer.Deserialize<JsonElement>(json);

    // Se erro de autenticação, tenta refresh
    if (!result.GetProperty("ok").GetBoolean())
    {
        var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
        if ((error == "invalid_auth" || error == "token_expired") && tokenRefreshService != null)
        {
            if (await tokenRefreshService.RefreshTokenAsync(teamId))
            {
                token = await tokenRepository.GetTokenAsync(teamId);
                if (token != null)
                {
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                    response = await httpClient.PostAsync("https://slack.com/api/users.list", null);
                    json = await response.Content.ReadAsStringAsync();
                }
            }
        }
    }

    return Results.Content(json, "application/json");
});

// Endpoint para desconectar team
app.MapDelete("/slack/team/{teamId}", async (string teamId, HttpClient httpClient, ISlackTokenRepository tokenRepository) =>
{
    var token = await tokenRepository.GetTokenAsync(teamId);
    if (token == null) return Results.BadRequest("Token não encontrado.");

    // Revoga o token via auth.revoke
    httpClient.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", token.AccessToken);

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

// Endpoint para home do Slack
app.MapPost("/slack/home", async (string teamId, string userId, HttpClient http, ISlackTokenRepository tokenRepository, ISlackTokenRefreshService? tokenRefreshService) =>
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
    var json = await resp.Content.ReadAsStringAsync();
    var result = JsonSerializer.Deserialize<JsonElement>(json);

    // Se erro de autenticação, tenta refresh
    if (!result.GetProperty("ok").GetBoolean())
    {
        var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
        if ((error == "invalid_auth" || error == "token_expired") && tokenRefreshService != null)
        {
            if (await tokenRefreshService.RefreshTokenAsync(teamId))
            {
                token = await tokenRepository.GetTokenAsync(teamId);
                if (token != null)
                {
                    http.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
                    resp = await http.PostAsync("https://slack.com/api/views.publish", JsonContent.Create(home));
                    json = await resp.Content.ReadAsStringAsync();
                }
            }
        }
    }

    return Results.Content(json, "application/json");
});

// Endpoint para entrar em canal
app.MapPost("/slack/join/{channelId}", async (string teamId, string channelId, HttpClient http, ISlackTokenRepository tokenRepository, ISlackTokenRefreshService? tokenRefreshService) =>
{
    var token = await tokenRepository.GetTokenAsync(teamId);
    if (token == null) return Results.BadRequest("Token não encontrado.");

    http.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);

    var resp = await http.PostAsync($"https://slack.com/api/conversations.join?channel={channelId}", null);
    var json = await resp.Content.ReadAsStringAsync();
    var result = JsonSerializer.Deserialize<JsonElement>(json);

    // Se erro de autenticação, tenta refresh
    if (!result.GetProperty("ok").GetBoolean())
    {
        var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
        if ((error == "invalid_auth" || error == "token_expired") && tokenRefreshService != null)
        {
            if (await tokenRefreshService.RefreshTokenAsync(teamId))
            {
                token = await tokenRepository.GetTokenAsync(teamId);
                if (token != null)
                {
                    http.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
                    resp = await http.PostAsync($"https://slack.com/api/conversations.join?channel={channelId}", null);
                    json = await resp.Content.ReadAsStringAsync();
                }
            }
        }
    }

    return Results.Content(json, "application/json");
});

// Endpoint para criar lembrete
app.MapPost("/slack/reminders", async (
    CreateReminderRequest req, 
    HttpContext ctx,
    IReminderService reminderService) =>
{
    var teamId = ctx.Request.Headers["X-Team-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(teamId))
        return Results.BadRequest(new CreateReminderResult(false, "Team ID não fornecido no header X-Team-Id."));

    try
    {
        var reminderId = await reminderService.CreateReminderAsync(teamId, req.UserId, req.Message, req.DueDate, req.ChannelId);

        Console.WriteLine($"[INFO] Lembrete criado: Id={reminderId}, TeamId={teamId}, UserId={req.UserId}, DueDate (BR)={req.DueDate:dd/MM/yyyy HH:mm}");

        return Results.Ok(new CreateReminderResult(
            Success: true,
            Message: $"Lembrete criado com sucesso! Data (Brasília): {req.DueDate:dd/MM/yyyy HH:mm}",
            ReminderId: reminderId
        ));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new CreateReminderResult(false, ex.Message));
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
    IReminderService reminderService) =>
{
    var teamId = ctx.Request.Headers["X-Team-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(teamId))
        return Results.BadRequest(new { Message = "Team ID não fornecido no header X-Team-Id." });

    try
    {
        var reminders = await reminderService.GetRemindersByTeamAsync(teamId);
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
    IReminderService reminderService) =>
{
    var teamId = ctx.Request.Headers["X-Team-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(teamId))
        return Results.BadRequest(new { Message = "Team ID não fornecido no header X-Team-Id." });

    try
    {
        var reminders = await reminderService.GetRemindersByUserAsync(teamId, userId);
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

// Endpoint para interações do Slack (botões e modais)
app.MapPost("/slack/interactive", async (
    HttpRequest request,
    ISlackInteractiveService interactiveService) =>
{
    var result = await interactiveService.HandleInteractiveEventAsync(request);
    
    if (result.StatusCode == 400)
        return Results.BadRequest(result.ErrorMessage);
    
    if (result.Data != null)
        return Results.Ok(result.Data);
    
    return Results.Ok();
});

// Health check endpoints
app.MapHealthChecks("/health/liveness", new()
{
    Predicate = check => check.Tags.Contains("liveness")
});

app.MapHealthChecks("/health", new()
{
    Predicate = _ => true
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
