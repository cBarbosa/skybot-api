using Microsoft.AspNetCore.Http;
using skybot.Core.Helpers;
using skybot.Core.Interfaces;
using skybot.Core.Models;
using skybot.Core.Models.Commands;
using skybot.Core.Services.Infrastructure;

namespace skybot.Core.Services;

public class CommandService : ICommandService
{
    private readonly ISlackService _slackService;
    private readonly ISlackBlockBuilderService _blockBuilder;
    private readonly ICommandInteractionRepository _commandInteractionRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    private static readonly string[] HelpCommands = new[]
    {
        "!ajuda – Mostra esta ajuda",
        "!ping – Responde pong!",
        "!horario – Mostra a hora atual",
        "!canal <nome> – Cria um canal público",
        "!membros – Lista membros do canal",
        "!lembretes – Gerencia lembretes (botões interativos)"
    };

    public CommandService(
        ISlackService slackService, 
        ISlackBlockBuilderService blockBuilder,
        ICommandInteractionRepository commandInteractionRepo,
        IHttpContextAccessor httpContextAccessor)
    {
        _slackService = slackService;
        _blockBuilder = blockBuilder;
        _commandInteractionRepo = commandInteractionRepo;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task ExecuteCommandAsync(string command, string args, SlackEvent slackEvent, string accessToken, string teamId)
    {
        var commandKey = command.ToLowerInvariant();
        bool success = true;
        string? errorMessage = null;
        
        try
        {
            switch (commandKey)
            {
                case "!ajuda":
                case "ajuda":
                    await ShowHelpAsync(slackEvent, accessToken);
                    break;
                case "!ping":
                case "ping":
                    await _slackService.SendMessageAsync(accessToken, slackEvent.Channel, "pong!", slackEvent.Ts);
                    break;
                case "!horario":
                case "horario":
                    await ShowTimeAsync(slackEvent, accessToken);
                    break;
                case "!canal":
                case "canal":
                    await CreateChannelAsync(teamId, args, slackEvent, accessToken);
                    break;
                case "!membros":
                case "membros":
                    await ListMembersAsync(teamId, slackEvent.Channel, slackEvent, accessToken);
                    break;
                case "!lembretes":
                case "lembretes":
                    await ShowRemindersMenuAsync(slackEvent, accessToken);
                    break;
                default:
                    success = false;
                    errorMessage = $"Comando '{command}' não encontrado";
                    await _slackService.SendMessageAsync(accessToken, slackEvent.Channel, 
                        $"❌ {errorMessage}. Use !ajuda para ver os comandos disponíveis.", 
                        slackEvent.Ts);
                    break;
            }
        }
        catch (Exception ex)
        {
            success = false;
            errorMessage = ex.Message;
            throw;
        }
        finally
        {
            // Registra a interação com o comando (não-bloqueante)
            await LogCommandInteractionAsync(commandKey, args, slackEvent, teamId, success, errorMessage);
        }
    }

    public async Task ShowHelpAsync(SlackEvent slackEvent, string accessToken)
    {
        var help = string.Join("\n", HelpCommands);
        await _slackService.SendMessageAsync(accessToken, slackEvent.Channel, help, slackEvent.Ts);
    }

    public async Task ShowTimeAsync(SlackEvent slackEvent, string accessToken)
    {
        var now = TimezoneHelper.GetBrazilianTime();
        await _slackService.SendMessageAsync(accessToken, slackEvent.Channel, 
            $"Agora são {now:HH:mm} (horário de Brasília - UTC-3)", slackEvent.Ts);
    }

    public async Task CreateChannelAsync(string teamId, string name, SlackEvent slackEvent, string accessToken)
    {
        if (string.IsNullOrEmpty(teamId))
        {
            await _slackService.SendMessageAsync(accessToken, slackEvent.Channel, "Erro: Team ID não encontrado.", slackEvent.Ts);
            return;
        }
        var result = await _slackService.CreateChannelAsync(teamId, name);
        await _slackService.SendMessageAsync(accessToken, slackEvent.Channel, result.Message, slackEvent.Ts);
    }

    public async Task ListMembersAsync(string teamId, string channelId, SlackEvent slackEvent, string accessToken, int maxMembers = 10)
    {
        if (string.IsNullOrEmpty(teamId))
        {
            await _slackService.SendMessageAsync(accessToken, slackEvent.Channel, "Erro: Team ID não encontrado.", slackEvent.Ts);
            return;
        }
        var result = await _slackService.ListChannelMembersAsync(teamId, channelId, maxMembers: maxMembers);
        await _slackService.SendMessageAsync(accessToken, slackEvent.Channel, result.Message, slackEvent.Ts);
    }

    public async Task ShowRemindersMenuAsync(SlackEvent slackEvent, string accessToken)
    {
        if (string.IsNullOrEmpty(slackEvent.User))
        {
            await _slackService.SendMessageAsync(accessToken, slackEvent.Channel, "Erro: User ID não encontrado.", slackEvent.Ts);
            return;
        }

        var blocks = _blockBuilder.CreateRemindersMenuBlocks(slackEvent.User);
        await _slackService.SendBlocksAsync(accessToken, slackEvent.Channel, blocks, slackEvent.Ts);
    }

    private async Task LogCommandInteractionAsync(
        string command, 
        string? args, 
        SlackEvent slackEvent, 
        string teamId,
        bool success,
        string? errorMessage)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var request = new CreateCommandInteractionRequest(
                TeamId: teamId,
                UserId: slackEvent.User ?? "unknown",
                InteractionType: InteractionType.COMMAND,
                Command: command,
                ActionId: null,
                Arguments: args,
                Channel: slackEvent.Channel,
                ThreadTs: slackEvent.ThreadTs,
                MessageTs: slackEvent.Ts,
                SourceIp: httpContext != null ? HttpContextHelper.GetSourceIp(httpContext) : null,
                UserAgent: httpContext != null ? HttpContextHelper.GetUserAgent(httpContext) : null,
                Success: success,
                ErrorMessage: errorMessage
            );

            await _commandInteractionRepo.CreateAsync(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Falha ao registrar interação com comando: {ex.Message}");
            // Não propaga o erro para não afetar a execução do comando
        }
    }
}

