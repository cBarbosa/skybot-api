using System.Net.Http.Headers;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
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
        c.RoutePrefix = "swagger"; // Swagger em /swagger
    });
}

app.UseCors("SkybotPolicy");
app.UseMiddleware<skybot.API.Middleware.ExceptionHandlingMiddleware>();
app.UseMiddleware<skybot.API.Middleware.ApiKeyAuthenticationMiddleware>();

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
app.MapGet("/slack/oauth", async (string code, string? state, HttpClient httpClient, ISlackTokenRepository tokenRepository, IApiKeyRepository apiKeyRepository, IConfiguration config) =>
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

        // Gera API Key automaticamente para o workspace
        var apiKey = await apiKeyRepository.CreateAsync(
            oauthResponse.Team!.Id, 
            "Chave Principal - Gerada na Instalação", 
            allowedEndpoints: null, 
            expiresAt: null);

        // Cria configurações do workspace com AdminUserId
        var adminUserId = oauthResponse.AuthedUser?.Id;
        if (!string.IsNullOrEmpty(adminUserId))
        {
            var workspaceSettingsRepo = app.Services.GetRequiredService<IWorkspaceSettingsRepository>();
            await workspaceSettingsRepo.CreateAsync(oauthResponse.Team!.Id, adminUserId);
            Console.WriteLine($"[INFO] Configurações do workspace criadas para team {oauthResponse.Team!.Id} com admin {adminUserId}");
        }

        // Envia mensagem DM para o administrador do workspace com a API Key
        if (!string.IsNullOrEmpty(adminUserId))
        {
            try
            {
                var dmMessage = new
                {
                    channel = adminUserId,
                    text = $"🎉 *Skybot instalado com sucesso!*\n\n" +
                           $"Sua API Key para integrações externas foi gerada:\n\n" +
                           $"```{apiKey}```\n\n" +
                           $"⚠️ *IMPORTANTE:*\n" +
                           $"• Guarde esta chave em local seguro\n" +
                           $"• Use o header `X-Api-Key` em todas as requisições\n" +
                           $"• Esta chave não será exibida novamente\n" +
                           $"• Você pode gerar novas chaves através do endpoint `/api/keys`\n\n" +
                           $"📚 Documentação: https://skyapi.skymedia.com.br"
                };

                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oauthResponse.AccessToken);
                var dmResponse = await httpClient.PostAsync("https://slack.com/api/chat.postMessage", 
                    JsonContent.Create(dmMessage));
                
                var dmResult = await dmResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"[INFO] Mensagem DM enviada para {adminUserId}: {dmResult}");
            }
            catch (Exception dmEx)
            {
                Console.WriteLine($"[WARN] Falha ao enviar DM com API Key: {dmEx.Message}");
            }
        }

        return Results.Ok(new
        {
            Message = "App instalado com sucesso! Verifique suas mensagens diretas para obter sua API Key.",
            TeamId = oauthResponse.Team!.Id,
            TeamName = oauthResponse.Team!.Name,
            ApiKeyGenerated = true
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
app.MapGet("/slack/channels", async (HttpContext ctx, HttpClient httpClient, ISlackTokenRepository tokenRepository, ISlackTokenRefreshService? tokenRefreshService) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

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
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

    var result = await slackService.CreateChannelAsync(teamId, req.Name);
    
    return result.Success 
        ? Results.Ok(result) 
        : Results.BadRequest(result);
});

// Endpoint para listar membros do canal
app.MapGet("/slack/channels/{channelId}/members", async (string channelId, HttpContext ctx, ISlackService slackService) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

    var result = await slackService.ListChannelMembersAsync(teamId, channelId, maxMembers: 50);
    
    return result.Success 
        ? Results.Ok(result) 
        : Results.BadRequest(result);
});

// Endpoint para listar usuários
app.MapGet("/slack/users", async (HttpContext ctx, HttpClient httpClient, ISlackTokenRepository tokenRepository, ISlackTokenRefreshService? tokenRefreshService) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

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
app.MapDelete("/slack/team", async (HttpContext ctx, HttpClient httpClient, ISlackTokenRepository tokenRepository) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

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
app.MapPost("/slack/home", async (string userId, HttpContext ctx, HttpClient http, ISlackTokenRepository tokenRepository, ISlackTokenRefreshService? tokenRefreshService) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

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
app.MapPost("/slack/join/{channelId}", async (string channelId, HttpContext ctx, HttpClient http, ISlackTokenRepository tokenRepository, ISlackTokenRefreshService? tokenRefreshService) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

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
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

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
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

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
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

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

// Endpoints de gerenciamento de API Keys
app.MapPost("/api/keys", async (skybot.Core.Models.Auth.CreateApiKeyRequest req, IApiKeyRepository apiKeyRepo) =>
{
    try
    {
        var apiKey = await apiKeyRepo.CreateAsync(req.TeamId, req.Name, req.AllowedEndpoints, req.ExpiresAt);
        
        return Results.Ok(new 
        { 
            success = true, 
            apiKey = apiKey,
            message = "IMPORTANTE: Guarde esta chave! Ela não será exibida novamente."
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, message = ex.Message });
    }
});

app.MapGet("/api/keys", async (HttpContext ctx, IApiKeyRepository apiKeyRepo) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

    var keys = await apiKeyRepo.GetByTeamIdAsync(teamId);
    
    // Não retorna as chaves completas, apenas metadata
    var safeKeys = keys.Select(k => new
    {
        k.Id,
        k.Name,
        k.IsActive,
        k.AllowedEndpoints,
        k.CreatedAt,
        k.LastUsedAt,
        k.ExpiresAt,
        KeyPreview = k.Key.Length > 8 ? $"{k.Key[..4]}...{k.Key[^4..]}" : "****"
    });

    return Results.Ok(new { success = true, keys = safeKeys });
});

app.MapDelete("/api/keys/{apiKey}", async (string apiKey, HttpContext ctx, IApiKeyRepository apiKeyRepo) =>
{
    // Verifica se a chave pertence ao mesmo team
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

    var keyToRevoke = await apiKeyRepo.GetByKeyAsync(apiKey);
    if (keyToRevoke == null)
        return Results.NotFound(new { success = false, message = "API Key não encontrada." });

    if (keyToRevoke.TeamId != teamId)
        return Results.Forbid();

    var revoked = await apiKeyRepo.RevokeAsync(apiKey);
    return revoked 
        ? Results.Ok(new { success = true, message = "API Key revogada com sucesso." })
        : Results.NotFound(new { success = false, message = "API Key não encontrada." });
});

// Endpoint: Enviar mensagem para o Slack
app.MapPost("/slack/messages", async (
    HttpContext ctx,
    skybot.Core.Models.Slack.SendMessageRequest request,
    ISlackService slackService,
    IMessageLogRepository messageLogRepo,
    IApiKeyRepository apiKeyRepo,
    IValidator<skybot.Core.Models.Slack.SendMessageRequest> validator) =>
{
    // Obter TeamId do contexto (autenticado via API Key middleware)
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
    {
        return Results.Json(
            new { success = false, error = "Autenticação necessária (X-Api-Key)" },
            statusCode: 401
        );
    }

    // Validar request
    var validationResult = await validator.ValidateAsync(request);
    if (!validationResult.IsValid)
    {
        var errors = validationResult.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage });
        return Results.Json(
            new { success = false, errors },
            statusCode: 400
        );
    }

    // Obter informações da API Key
    var apiKeyHeader = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    var apiKey = apiKeyHeader != null ? await apiKeyRepo.GetByKeyAsync(apiKeyHeader) : null;

    // Obter informações HTTP para auditoria
    var sourceIp = skybot.Core.Helpers.HttpContextHelper.GetSourceIp(ctx);
    var forwardedFor = skybot.Core.Helpers.HttpContextHelper.GetForwardedFor(ctx);
    var userAgent = skybot.Core.Helpers.HttpContextHelper.GetUserAgent(ctx);
    var referer = skybot.Core.Helpers.HttpContextHelper.GetReferer(ctx);
    var requestId = skybot.Core.Helpers.HttpContextHelper.GenerateRequestId();

    Console.WriteLine($"[INFO] Request ID: {requestId} | IP: {sourceIp} | Forwarded: {forwardedFor} | UA: {userAgent}");

    // Enviar mensagem
    var result = await slackService.SendMessageAsync(teamId, request);

    if (!result.Success)
    {
        return Results.Json(
            new { success = false, error = result.Error },
            statusCode: 400
        );
    }

    // Registrar no log (não bloqueia em caso de erro)
    try
    {
        await messageLogRepo.CreateLogAsync(new skybot.Core.Models.Slack.CreateMessageLogRequest(
            TeamId: teamId,
            MessageTs: result.MessageTs!,
            Channel: request.DestinationId,
            DestinationType: request.DestinationType,
            ThreadTs: request.ThreadTs,
            ApiKeyId: apiKey?.Id,
            ApiKeyName: apiKey?.Name,
            SourceIp: sourceIp,
            ForwardedFor: forwardedFor,
            UserAgent: userAgent,
            Referer: referer,
            RequestId: requestId,
            ContentType: request.Text != null ? skybot.Core.Models.Slack.MessageContentType.TEXT : skybot.Core.Models.Slack.MessageContentType.BLOCKS,
            HasAttachments: false
        ));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARNING] Falha ao registrar log de mensagem: {ex.Message}");
    }

    return Results.Ok(new
    {
        success = true,
        messageTs = result.MessageTs,
        requestId = requestId,
        message = "Mensagem enviada com sucesso"
    });
});

// Endpoint: Apagar mensagem do Slack
app.MapDelete("/slack/messages", async (
    HttpContext ctx,
    [FromBody] skybot.Core.Models.Slack.DeleteMessageRequest request,
    ISlackService slackService,
    IMessageLogRepository messageLogRepo) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
    {
        return Results.Json(
            new { success = false, error = "Autenticação necessária (X-Api-Key)" },
            statusCode: 401
        );
    }

    // Apagar mensagem
    var result = await slackService.DeleteMessageAsync(teamId, request.Channel, request.MessageTs);

    if (!result.Success)
    {
        return Results.Json(
            new { success = false, error = result.Error },
            statusCode: 400
        );
    }

    // Marcar como deletada no log (não falha se houver erro no log)
    try
    {
        await messageLogRepo.MarkAsDeletedAsync(request.MessageTs);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARNING] Falha ao atualizar log de mensagem: {ex.Message}");
    }

    return Results.Ok(new
    {
        success = true,
        message = "Mensagem apagada com sucesso"
    });
});

// Endpoint: Relatório diário de mensagens
app.MapGet("/slack/messages/reports/daily", async (
    HttpContext ctx,
    IMessageLogRepository messageLogRepo,
    DateTime? date) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

    var targetDate = date ?? DateTime.UtcNow;
    var stats = await messageLogRepo.GetDailyStatsAsync(teamId, targetDate);
    
    return Results.Ok(new
    {
        date = targetDate.ToString("yyyy-MM-dd"),
        statistics = stats,
        total = stats.Values.Sum()
    });
});

// Endpoint: Relatório semanal
app.MapGet("/slack/messages/reports/weekly", async (
    HttpContext ctx,
    IMessageLogRepository messageLogRepo,
    DateTime? startDate) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

    var targetDate = startDate ?? DateTime.UtcNow.AddDays(-7);
    var stats = await messageLogRepo.GetWeeklyStatsAsync(teamId, targetDate);
    
    return Results.Ok(new
    {
        startDate = targetDate.ToString("yyyy-MM-dd"),
        endDate = targetDate.AddDays(7).ToString("yyyy-MM-dd"),
        dailyStatistics = stats,
        total = stats.Values.Sum()
    });
});

// Endpoint: Relatório mensal
app.MapGet("/slack/messages/reports/monthly", async (
    HttpContext ctx,
    IMessageLogRepository messageLogRepo,
    int? year,
    int? month) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

    var targetYear = year ?? DateTime.UtcNow.Year;
    var targetMonth = month ?? DateTime.UtcNow.Month;
    
    var stats = await messageLogRepo.GetMonthlyStatsAsync(teamId, targetYear, targetMonth);
    
    return Results.Ok(new
    {
        year = targetYear,
        month = targetMonth,
        dailyStatistics = stats,
        total = stats.Values.Sum()
    });
});

// Endpoint: Auditoria de mensagens
app.MapGet("/slack/messages/logs/audit", async (
    HttpContext ctx,
    IMessageLogRepository messageLogRepo,
    string? ip,
    string? apiKeyName,
    DateTime? startDate,
    DateTime? endDate) =>
{
    var teamId = ctx.Items["TeamId"]?.ToString();
    if (string.IsNullOrEmpty(teamId))
        return Results.Unauthorized();

    var logs = await messageLogRepo.GetAuditLogsAsync(
        teamId, 
        ip, 
        apiKeyName,
        startDate ?? DateTime.UtcNow.AddDays(-7),
        endDate ?? DateTime.UtcNow
    );
    
    return Results.Ok(new
    {
        count = logs.Count,
        logs = logs.Select(log => new
        {
            log.MessageTs,
            log.Channel,
            log.DestinationType,
            log.SentAt,
            log.SourceIp,
            log.ForwardedFor,
            log.UserAgent,
            log.ApiKeyName,
            log.Status
        })
    });
});

app.Run();
