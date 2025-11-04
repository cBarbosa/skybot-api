using skybot.Core.Interfaces;
using skybot.Core.Models;
using skybot.Core.Services.Infrastructure;

namespace skybot.Core.Services;

public class CommandService : ICommandService
{
    private readonly ISlackService _slackService;
    private readonly ISlackBlockBuilderService _blockBuilder;
    private static readonly string[] HelpCommands = new[]
    {
        "!ajuda – Mostra esta ajuda",
        "!ping – Responde pong!",
        "!horario – Mostra a hora atual",
        "!canal <nome> – Cria um canal público",
        "!membros – Lista membros do canal",
        "!lembretes – Gerencia lembretes (botões interativos)"
    };

    public CommandService(ISlackService slackService, ISlackBlockBuilderService blockBuilder)
    {
        _slackService = slackService;
        _blockBuilder = blockBuilder;
    }

    public async Task ExecuteCommandAsync(string command, string args, SlackEvent slackEvent, string accessToken, string teamId)
    {
        var commandKey = command.ToLowerInvariant();
        
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
                await _slackService.SendMessageAsync(accessToken, slackEvent.Channel, 
                    $"❌ Comando '{command}' não encontrado. Use !ajuda para ver os comandos disponíveis.", 
                    slackEvent.Ts);
                break;
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
}

