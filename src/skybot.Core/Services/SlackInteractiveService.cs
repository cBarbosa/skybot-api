using System.Net.Http.Headers;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using skybot.Core.Helpers;
using skybot.Core.Interfaces;
using skybot.Core.Models.Commands;
using skybot.Core.Models.Common;
using skybot.Core.Models.Reminders;
using skybot.Core.Services.Infrastructure;
using skybot.Core.Validators;

namespace skybot.Core.Services;

public class SlackInteractiveService : ISlackInteractiveService
{
    private readonly ISlackService _slackService;
    private readonly ISlackTokenRepository _tokenRepository;
    private readonly IReminderRepository _reminderRepository;
    private readonly IAIService _aiService;
    private readonly ICacheService _cacheService;
    private readonly ISlackBlockBuilderService _blockBuilder;
    private readonly IValidator<ReminderModalSubmission> _modalValidator;
    private readonly ICommandInteractionRepository _commandInteractionRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SlackInteractiveService(
        ISlackService slackService,
        ISlackTokenRepository tokenRepository,
        IReminderRepository reminderRepository,
        IAIService aiService,
        ICacheService cacheService,
        ISlackBlockBuilderService blockBuilder,
        IValidator<ReminderModalSubmission> modalValidator,
        ICommandInteractionRepository commandInteractionRepo,
        IHttpContextAccessor httpContextAccessor)
    {
        _slackService = slackService;
        _tokenRepository = tokenRepository;
        _reminderRepository = reminderRepository;
        _aiService = aiService;
        _cacheService = cacheService;
        _blockBuilder = blockBuilder;
        _modalValidator = modalValidator;
        _commandInteractionRepo = commandInteractionRepo;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<ServiceResult> HandleInteractiveEventAsync(HttpRequest request)
    {
        var formData = await request.ReadFormAsync();
        var payloadStr = formData["payload"].ToString();

        if (string.IsNullOrEmpty(payloadStr))
            return ServiceResult.BadRequest("Payload não fornecido");

        var payload = JsonSerializer.Deserialize<JsonElement>(payloadStr);
        var actionType = payload.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        switch (actionType)
        {
            case "block_actions":
                return await HandleBlockActionsAsync(payload);
            case "view_submission":
                return await HandleViewSubmissionAsync(payload);
            default:
                return ServiceResult.Ok();
        }
    }

    private async Task<ServiceResult> HandleBlockActionsAsync(JsonElement payload)
    {
        var teamId = payload.GetProperty("team").GetProperty("id").GetString();
        var user = payload.GetProperty("user").GetProperty("id").GetString();

        if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(user))
            return ServiceResult.BadRequest("Team ID ou User ID não encontrado");

        var token = await _tokenRepository.GetTokenAsync(teamId);
        if (token == null)
            return ServiceResult.BadRequest("Token não encontrado");

        var actions = payload.GetProperty("actions").EnumerateArray().First();
        var actionId = actions.GetProperty("action_id").GetString();
        var channel = payload.TryGetProperty("channel", out var ch) ? ch.GetProperty("id").GetString() : null;

        switch (actionId)
        {
            case "confirm_ai_yes":
                return await HandleConfirmAIYesAsync(actions, token.AccessToken, channel ?? user, user);
            case "confirm_ai_no":
                return await HandleConfirmAINoAsync(actions, token.AccessToken, channel ?? user, user);
            case "view_my_reminders":
                return await HandleViewMyRemindersAsync(teamId, user, token.AccessToken, channel ?? user);
            case "add_reminder_modal":
            case "send_reminder_modal":
                return await HandleOpenReminderModalAsync(actionId, payload, token.AccessToken);
            default:
                return ServiceResult.Ok();
        }
    }

    private async Task<ServiceResult> HandleConfirmAIYesAsync(JsonElement actions, string accessToken, string channel, string user)
    {
        var attemptKey = actions.GetProperty("value").GetString();
        if (string.IsNullOrEmpty(attemptKey))
            return ServiceResult.BadRequest("Chave de tentativa não encontrada");

        var pending = _cacheService.GetPendingAIMessage(attemptKey);
        if (!pending.HasValue)
        {
            await _slackService.SendMessageAsync(accessToken, channel, "A mensagem expirou. Por favor, envie novamente.", null);
            return ServiceResult.Ok();
        }

        // Registra a interação com o botão
        await LogInteractiveActionAsync("confirm_ai_yes", InteractionType.BUTTON, user, channel, pending.Value.ThreadTs, true);

        _cacheService.RemovePendingAIMessage(attemptKey);
        _cacheService.ResetCommandAttempts(attemptKey);
        _cacheService.SetThreadAIMode(attemptKey);

        await _slackService.SendMessageAsync(accessToken, channel,
            "✅ Você escolheu usar um agente virtual. A partir de agora, todas as mensagens nesta thread serão tratadas pelo agente virtual. Processando...",
            pending.Value.ThreadTs);

        await _slackService.SendMessageAsync(accessToken, channel, "🤔 Pensando...", pending.Value.ThreadTs);

        var aiResponse = await _aiService.GetAIResponseAsync(pending.Value.Message, null, attemptKey);

        if (!string.IsNullOrWhiteSpace(aiResponse))
        {
            await _slackService.SendMessageAsync(accessToken, channel, aiResponse, pending.Value.ThreadTs);
        }
        else
        {
            await _slackService.SendMessageAsync(accessToken, channel,
                "⚠️ Não há agentes virtuais disponíveis no momento. Tente novamente em instantes.",
                pending.Value.ThreadTs);
        }

        return ServiceResult.Ok();
    }

    private async Task<ServiceResult> HandleConfirmAINoAsync(JsonElement actions, string accessToken, string channel, string user)
    {
        var attemptKey = actions.GetProperty("value").GetString();
        if (string.IsNullOrEmpty(attemptKey))
            return ServiceResult.BadRequest("Chave de tentativa não encontrada");

        var pending = _cacheService.GetPendingAIMessage(attemptKey);
        if (!pending.HasValue)
        {
            await _slackService.SendMessageAsync(accessToken, channel, "A mensagem expirou. Por favor, envie novamente.", null);
            return ServiceResult.Ok();
        }

        // Registra a interação com o botão
        await LogInteractiveActionAsync("confirm_ai_no", InteractionType.BUTTON, user, channel, pending.Value.ThreadTs, true);

        _cacheService.RemovePendingAIMessage(attemptKey);
        _cacheService.ResetCommandAttempts(attemptKey);
        _cacheService.RemoveThreadAIMode(attemptKey);
        _aiService.ClearThreadProvider(attemptKey);

        await _slackService.SendMessageAsync(accessToken, channel,
            "❌ Você escolheu não usar o agente virtual.", pending.Value.ThreadTs);

        await _slackService.SendMessageAsync(accessToken, channel,
            "Entendido! Você pode tentar mais 3 vezes os comandos. Use !ajuda para ver os comandos disponíveis.",
            pending.Value.ThreadTs);

        return ServiceResult.Ok();
    }

    private async Task<ServiceResult> HandleViewMyRemindersAsync(string teamId, string userId, string accessToken, string channel)
    {
        // Registra a interação com o botão
        await LogInteractiveActionAsync("view_my_reminders", InteractionType.BUTTON, userId, channel, null, true, teamId);

        var reminders = await _reminderRepository.GetRemindersByUserAsync(teamId, userId, includeSent: false);

        if (reminders.Count == 0)
        {
            await _slackService.SendMessageAsync(accessToken, channel, "Você não tem lembretes pendentes.", null);
            return ServiceResult.Ok();
        }

        var remindersText = string.Join("\n", reminders.Select(r =>
        {
            var dueDateBr = r.DueDate.HasValue
                ? TimezoneHelper.ConvertToBrazilianTime(r.DueDate.Value)
                : DateTime.MinValue;
            return $"• *{dueDateBr:dd/MM/yyyy HH:mm}* - {r.Message}";
        }));

        await _slackService.SendMessageAsync(accessToken, channel,
            $"📋 *Seus Lembretes Pendentes:*\n{remindersText}", null);

        return ServiceResult.Ok();
    }

    private async Task<ServiceResult> HandleOpenReminderModalAsync(string actionId, JsonElement payload, string accessToken)
    {
        var teamId = payload.GetProperty("team").GetProperty("id").GetString();
        var user = payload.GetProperty("user").GetProperty("id").GetString();
        var channel = payload.TryGetProperty("channel", out var ch) ? ch.GetProperty("id").GetString() : user;

        // Registra a interação com o botão
        await LogInteractiveActionAsync(actionId, InteractionType.BUTTON, user ?? "unknown", channel ?? "unknown", null, true, teamId);

        var isForSomeone = actionId == "send_reminder_modal";
        var triggerId = payload.GetProperty("trigger_id").GetString();

        var modal = _blockBuilder.CreateReminderModal(isForSomeone, triggerId ?? "");

        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PostAsync("https://slack.com/api/views.open",
            System.Net.Http.Json.JsonContent.Create(new { trigger_id = triggerId, view = modal }));

        return ServiceResult.Ok();
    }

    private async Task<ServiceResult> HandleViewSubmissionAsync(JsonElement payload)
    {
        var teamId = payload.GetProperty("team").GetProperty("id").GetString();
        var user = payload.GetProperty("user").GetProperty("id").GetString();

        if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(user))
            return ServiceResult.BadRequest("Team ID ou User ID não encontrado");

        var token = await _tokenRepository.GetTokenAsync(teamId);
        if (token == null)
            return ServiceResult.BadRequest("Token não encontrado");

        var callbackId = payload.GetProperty("view").GetProperty("callback_id").GetString();
        var values = payload.GetProperty("view").GetProperty("state").GetProperty("values");

        string? targetUserId = callbackId == "send_reminder_submit"
            ? values.GetProperty("user_select").GetProperty("user").GetProperty("selected_user").GetString()
            : user;

        var date = values.GetProperty("date_input").GetProperty("date").GetProperty("selected_date").GetString();
        var time = values.GetProperty("time_input").GetProperty("time").GetProperty("value").GetString();
        var message = values.GetProperty("message_input").GetProperty("message").GetProperty("value").GetString();

        // Cria objeto de submissão do modal
        var submission = new ReminderModalSubmission(targetUserId, date ?? "", time ?? "", message ?? "");

        // Valida usando FluentValidation
        var validationResult = await _modalValidator.ValidateAsync(submission);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => GetFieldName(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.First().ErrorMessage);

            return ServiceResult.Ok(new { response_action = "errors", errors });
        }

        // Parse dos dados validados
        if (!TimeSpan.TryParse(time, out var timeSpan))
        {
            return ServiceResult.Ok(new { response_action = "errors", errors = new Dictionary<string, string> { { "time_input", "Formato de hora inválido. Use HH:mm" } } });
        }

        var dueDate = DateTime.Parse($"{date} {time}");
        dueDate = new DateTime(dueDate.Year, dueDate.Month, dueDate.Day, timeSpan.Hours, timeSpan.Minutes, 0);

        var dueDateUtc = TimezoneHelper.ConvertToUtc(dueDate);

        try
        {
            // Valida targetUserId apenas se for envio para outra pessoa
            if (callbackId == "send_reminder_submit" && string.IsNullOrEmpty(targetUserId))
                return ServiceResult.Ok(new { response_action = "errors", errors = new Dictionary<string, string> { { "user_select", "Usuário não selecionado" } } });

            // Após validação, sabemos que message e targetUserId não são null
            var reminderId = await _reminderRepository.CreateReminderAsync(teamId, targetUserId ?? user, message ?? "", dueDateUtc);

            var dueDateBrFormatted = TimezoneHelper.ConvertToBrazilianTime(dueDateUtc);
            var dueDateFormatted = $"{dueDateBrFormatted:dd/MM/yyyy HH:mm}";

            var successMessage = callbackId == "send_reminder_submit"
                ? $"✅ Lembrete criado com sucesso!\n\n📅 *Data/Hora:* {dueDateFormatted}\n💬 *Mensagem:* {message}\n👤 *Enviar para:* <@{targetUserId}>"
                : $"✅ Lembrete criado com sucesso!\n\n📅 *Data/Hora:* {dueDateFormatted}\n💬 *Mensagem:* {message}";

            _ = Task.Run(async () =>
            {
                try
                {
                    await _slackService.SendMessageAsync(token.AccessToken, user, successMessage, null);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Erro ao enviar confirmação de lembrete: {ex.Message}");
                }
            });

            return ServiceResult.Ok(new { response_action = "clear" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Erro ao criar lembrete: {ex.Message}");
            Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");

            var errorMessage = $"❌ Falha ao criar lembrete.\n\n*Erro:* {ex.Message}\n\nPor favor, tente novamente.";
            _ = Task.Run(async () =>
            {
                try
                {
                    await _slackService.SendMessageAsync(token.AccessToken, user, errorMessage, null);
                }
                catch (Exception sendEx)
                {
                    Console.WriteLine($"[ERROR] Erro ao enviar mensagem de erro: {sendEx.Message}");
                }
            });

            return ServiceResult.Ok(new { response_action = "errors", errors = new Dictionary<string, string> { { "message_input", $"Erro: {ex.Message}" } } });
        }
    }

    private static string GetFieldName(string? propertyName)
    {
        return propertyName switch
        {
            nameof(ReminderModalSubmission.Message) => "message_input",
            nameof(ReminderModalSubmission.Time) => "time_input",
            nameof(ReminderModalSubmission.Date) => "date_input",
            nameof(ReminderModalSubmission.TargetUserId) => "user_select",
            _ => "general"
        };
    }

    private async Task LogInteractiveActionAsync(
        string actionId,
        InteractionType interactionType,
        string userId,
        string channel,
        string? threadTs,
        bool success,
        string? teamId = null)
    {
        try
        {
            // Tenta extrair teamId do contexto se não foi fornecido
            if (string.IsNullOrEmpty(teamId))
            {
                // Não podemos registrar sem teamId
                return;
            }

            var httpContext = _httpContextAccessor.HttpContext;
            var request = new CreateCommandInteractionRequest(
                TeamId: teamId,
                UserId: userId,
                InteractionType: interactionType,
                Command: null,
                ActionId: actionId,
                Arguments: null,
                Channel: channel,
                ThreadTs: threadTs,
                MessageTs: DateTime.UtcNow.Ticks.ToString(),
                SourceIp: httpContext != null ? HttpContextHelper.GetSourceIp(httpContext) : null,
                UserAgent: httpContext != null ? HttpContextHelper.GetUserAgent(httpContext) : null,
                Success: success,
                ErrorMessage: null
            );

            await _commandInteractionRepo.CreateAsync(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Falha ao registrar interação interativa: {ex.Message}");
            // Não propaga o erro para não afetar a funcionalidade
        }
    }
}

