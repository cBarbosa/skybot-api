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
    new { Name = "!membros", Description = "Lista membros do canal" },
    new { Name = "lembrete", Description = "Gerencia lembretes (botões interativos)" }
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
    },

    ["!lembrete"] = async (evt, _, slackClient, slackService) =>
    {
        if (string.IsNullOrEmpty(evt.TeamId))
        {
            await slackService.SendMessageAsync(evt.AccessToken, evt.Channel, "Erro: Team ID não encontrado.", evt.Ts);
            return;
        }

        var blocks = new object[]
        {
            new
            {
                type = "section",
                text = new { type = "mrkdwn", text = "🔔 *Gerenciar Lembretes*\nEscolha uma opção:" }
            },
            new
            {
                type = "actions",
                elements = new object[]
                {
                    new
                    {
                        type = "button",
                        text = new { type = "plain_text", text = "📋 Ver Meus Lembretes" },
                        action_id = "view_my_reminders",
                        value = evt.User
                    },
                    new
                    {
                        type = "button",
                        text = new { type = "plain_text", text = "➕ Adicionar Lembrete" },
                        action_id = "add_reminder_modal",
                        value = evt.User
                    },
                    new
                    {
                        type = "button",
                        text = new { type = "plain_text", text = "📤 Enviar para Alguém" },
                        action_id = "send_reminder_modal",
                        value = evt.User
                    }
                }
            }
        };

        await slackService.SendBlocksAsync(evt.AccessToken, evt.Channel, blocks, evt.Ts);
    },

    ["lembrete"] = async (evt, _, slackClient, slackService) =>
    {
        if (string.IsNullOrEmpty(evt.TeamId))
        {
            await slackService.SendMessageAsync(evt.AccessToken, evt.Channel, "Erro: Team ID não encontrado.", evt.Ts);
            return;
        }

        var blocks = new object[]
        {
            new
            {
                type = "section",
                text = new { type = "mrkdwn", text = "🔔 *Gerenciar Lembretes*\nEscolha uma opção:" }
            },
            new
            {
                type = "actions",
                elements = new object[]
                {
                    new
                    {
                        type = "button",
                        text = new { type = "plain_text", text = "📋 Ver Meus Lembretes" },
                        action_id = "view_my_reminders",
                        value = evt.User
                    },
                    new
                    {
                        type = "button",
                        text = new { type = "plain_text", text = "➕ Adicionar Lembrete" },
                        action_id = "add_reminder_modal",
                        value = evt.User
                    },
                    new
                    {
                        type = "button",
                        text = new { type = "plain_text", text = "📤 Enviar para Alguém" },
                        action_id = "send_reminder_modal",
                        value = evt.User
                    }
                }
            }
        };

        await slackService.SendBlocksAsync(evt.AccessToken, evt.Channel, blocks, evt.Ts);
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

    // Aceita eventos do tipo "message" ou "app_mentions"
    if (evt == null || (evt.Type != "message" && evt.Type != "app_mentions"))
        return Results.Ok();

    // Filtra mensagens de bot
    if (evt.Subtype == "bot_message" || evt.BotId != null)
        return Results.Ok();

    // Se não tem usuário (não é uma mensagem válida), ignora
    if (string.IsNullOrEmpty(evt.User))
        return Results.Ok();

    var teamId = eventWrapper.TeamId;
    var token = await tokenRepository.GetTokenAsync(teamId);
    if (token == null) return Results.Ok();

    // Processa o texto removendo menções ao bot
    var rawText = evt.Text?.Trim() ?? "";
    
    // Remove menções ao bot do texto (ex: <@U123456> ou <@BOT_ID|nome>)
    // Padrão: <@ID> ou <@ID|nome>
    var text = System.Text.RegularExpressions.Regex.Replace(rawText, @"<@[^>]+>", "").Trim();
    
    // Verifica se havia menção ao bot no texto original
    var hasMention = System.Text.RegularExpressions.Regex.IsMatch(rawText, @"<@[^>]+>");
    
    // Se após remover menções não sobrou nada, ignora
    if (string.IsNullOrEmpty(text))
        return Results.Ok();

    // Verifica se é uma menção ao bot (app_mentions OU mensagem com menção no texto)
    var isMention = evt.Type == "app_mentions" || hasMention;
    var startsWithCommand = text.StartsWith("!");
    
    // Se não for menção e não começar com "!", ignora
    if (!isMention && !startsWithCommand)
        return Results.Ok();

    // Se não começar com "!" mas é uma menção, adiciona para manter compatibilidade
    if (!startsWithCommand && isMention)
        text = "!" + text;

    var spaceIndex = text.IndexOf(' ');
    var commandKey = spaceIndex > 0 ? text[..spaceIndex] : text;
    var args = spaceIndex > 0 ? text[(spaceIndex + 1)..] : "";

    if (!commands.TryGetValue(commandKey, out var action))
        return Results.Ok();

    // Usa thread_ts se disponível, senão usa ts para responder na thread correta
    var threadTs = evt.ThreadTs ?? evt.Ts;

    // Passa slackService e slackClient para o comando, incluindo TeamId no evento
    // Atualiza o texto processado (sem menções) e usa threadTs
    await action(evt with { AccessToken = token.AccessToken, TeamId = teamId, Text = text, Ts = threadTs }, args, slackClient, slackService);

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

// Endpoint para interações do Slack (botões e modais)
app.MapPost("/slack/interactive", async (HttpRequest request, SlackService slackService, SlackTokenRepository tokenRepository, ReminderRepository reminderRepository) =>
{
    var formData = await request.ReadFormAsync();
    var payloadStr = formData["payload"].ToString();
    
    if (string.IsNullOrEmpty(payloadStr))
        return Results.BadRequest("Payload não fornecido");

    var payload = JsonSerializer.Deserialize<JsonElement>(payloadStr);
    var actionType = payload.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

    if (actionType == "block_actions")
    {
        var teamId = payload.GetProperty("team").GetProperty("id").GetString();
        var user = payload.GetProperty("user").GetProperty("id").GetString();
        var token = await tokenRepository.GetTokenAsync(teamId);
        
        if (token == null)
            return Results.BadRequest("Token não encontrado");

        var actions = payload.GetProperty("actions").EnumerateArray().First();
        var actionId = actions.GetProperty("action_id").GetString();
        var channel = payload.TryGetProperty("channel", out var ch) ? ch.GetProperty("id").GetString() : null;

        if (actionId == "view_my_reminders")
        {
            var reminders = await reminderRepository.GetRemindersByUserAsync(teamId, user, includeSent: false);
            
            if (reminders.Count == 0)
            {
                await slackService.SendMessageAsync(token.AccessToken, channel ?? user, 
                    "Você não tem lembretes pendentes.", null);
                return Results.Ok();
            }

            var remindersText = string.Join("\n", reminders.Select(r => 
            {
                var dueDateBr = r.DueDate.HasValue 
                    ? TimezoneHelper.ConvertToBrazilianTime(r.DueDate.Value)
                    : DateTime.MinValue;
                return $"• *{dueDateBr:dd/MM/yyyy HH:mm}* - {r.Message}";
            }));

            await slackService.SendMessageAsync(token.AccessToken, channel ?? user, 
                $"📋 *Seus Lembretes Pendentes:*\n{remindersText}", null);
        }
        else if (actionId == "add_reminder_modal" || actionId == "send_reminder_modal")
        {
            var isForSomeone = actionId == "send_reminder_modal";
            var now = TimezoneHelper.GetBrazilianTime();
            var defaultDate = now.AddHours(1).ToString("yyyy-MM-dd");
            var defaultTime = now.AddHours(1).ToString("HH:mm");

            var modalBlocks = new List<object>();

            if (isForSomeone)
            {
                modalBlocks.Add(new
                {
                    type = "input",
                    block_id = "user_select",
                    label = new { type = "plain_text", text = "Enviar para" },
                    element = new
                    {
                        type = "users_select",
                        action_id = "user",
                        placeholder = new { type = "plain_text", text = "Selecione um usuário" }
                    }
                });
            }

            modalBlocks.Add(new
            {
                type = "input",
                block_id = "date_input",
                label = new { type = "plain_text", text = "Data" },
                element = new
                {
                    type = "datepicker",
                    action_id = "date",
                    initial_date = defaultDate,
                    placeholder = new { type = "plain_text", text = "Selecione a data" }
                }
            });

            modalBlocks.Add(new
            {
                type = "input",
                block_id = "time_input",
                label = new { type = "plain_text", text = "Hora (HH:mm)" },
                element = new
                {
                    type = "plain_text_input",
                    action_id = "time",
                    initial_value = defaultTime,
                    placeholder = new { type = "plain_text", text = "14:30" }
                }
            });

            modalBlocks.Add(new
            {
                type = "input",
                block_id = "message_input",
                label = new { type = "plain_text", text = "Mensagem" },
                element = new
                {
                    type = "plain_text_input",
                    action_id = "message",
                    multiline = true,
                    placeholder = new { type = "plain_text", text = "Digite a mensagem do lembrete..." }
                },
                optional = false
            });

            var modal = new
            {
                type = "modal",
                callback_id = isForSomeone ? "send_reminder_submit" : "add_reminder_submit",
                title = new { type = "plain_text", text = isForSomeone ? "Enviar Lembrete" : "Criar Lembrete" },
                submit = new { type = "plain_text", text = "Criar" },
                close = new { type = "plain_text", text = "Cancelar" },
                blocks = modalBlocks
            };

            var triggerId = payload.GetProperty("trigger_id").GetString();
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            
            var response = await client.PostAsync("https://slack.com/api/views.open", 
                JsonContent.Create(new { trigger_id = triggerId, view = modal }));
            
            return Results.Ok();
        }
    }
    else if (actionType == "view_submission")
    {
        var teamId = payload.GetProperty("team").GetProperty("id").GetString();
        var user = payload.GetProperty("user").GetProperty("id").GetString();
        var token = await tokenRepository.GetTokenAsync(teamId);
        var callbackId = payload.GetProperty("view").GetProperty("callback_id").GetString();
        var values = payload.GetProperty("view").GetProperty("state").GetProperty("values");

        string? targetUserId = null;
        if (callbackId == "send_reminder_submit")
        {
            targetUserId = values.GetProperty("user_select").GetProperty("user").GetProperty("selected_user").GetString();
        }
        else
        {
            targetUserId = user; // Para o próprio usuário
        }

        var date = values.GetProperty("date_input").GetProperty("date").GetProperty("selected_date").GetString();
        var time = values.GetProperty("time_input").GetProperty("time").GetProperty("value").GetString();
        var message = values.GetProperty("message_input").GetProperty("message").GetProperty("value").GetString();

        if (string.IsNullOrWhiteSpace(message))
        {
            return Results.Ok(new { response_action = "errors", errors = new Dictionary<string, string> { { "message_input", "A mensagem é obrigatória" } } });
        }

        if (!TimeSpan.TryParse(time, out var timeSpan))
        {
            return Results.Ok(new { response_action = "errors", errors = new Dictionary<string, string> { { "time_input", "Formato de hora inválido. Use HH:mm" } } });
        }

        var dueDate = DateTime.Parse($"{date} {time}");
        var dueDateBr = TimezoneHelper.GetBrazilianTime();
        dueDate = new DateTime(dueDate.Year, dueDate.Month, dueDate.Day, timeSpan.Hours, timeSpan.Minutes, 0);
        
        if (dueDate <= dueDateBr)
        {
            return Results.Ok(new { response_action = "errors", errors = new Dictionary<string, string> { { "date_input", "A data/hora deve ser no futuro" } } });
        }

        var dueDateUtc = TimezoneHelper.ConvertToUtc(dueDate);
        
        try
        {
            var reminderId = await reminderRepository.CreateReminderAsync(teamId, targetUserId, message, dueDateUtc);
            
            return Results.Ok(new { response_action = "clear" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Erro ao criar lembrete: {ex.Message}");
            return Results.Ok(new { response_action = "errors", errors = new Dictionary<string, string> { { "message_input", $"Erro: {ex.Message}" } } });
        }
    }

    return Results.Ok();
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