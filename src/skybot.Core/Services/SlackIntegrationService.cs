using System.Text.Json;
using System.Text.RegularExpressions;
using skybot.Core.Interfaces;
using skybot.Core.Models;

namespace skybot.Core.Services;

public class SlackIntegrationService : ISlackIntegrationService
{
    private readonly ICacheService _cacheService;
    private readonly ISlackService _slackService;
    private readonly ICommandService _commandService;
    private readonly IAIService _aiService;
    private readonly ISlackTokenRepository _tokenRepository;

    public SlackIntegrationService(
        ICacheService cacheService,
        ISlackService slackService,
        ICommandService commandService,
        IAIService aiService,
        ISlackTokenRepository tokenRepository)
    {
        _cacheService = cacheService;
        _slackService = slackService;
        _commandService = commandService;
        _aiService = aiService;
        _tokenRepository = tokenRepository;
    }

    public async Task ProcessSlackEventAsync(SlackEventWrapper eventWrapper)
    {
        // URL Verification
        if (eventWrapper.Event?.Type == "url_verification")
        {
            // Este caso deve ser tratado antes de chegar aqui
            return;
        }

        var evt = eventWrapper.Event;
        if (evt == null)
            return;

        // Verifica se o evento já foi processado (deduplicação)
        if (!string.IsNullOrEmpty(eventWrapper.EventId))
        {
            if (_cacheService.IsEventProcessed(eventWrapper.EventId))
            {
                Console.WriteLine($"[INFO] Evento duplicado ignorado: {eventWrapper.EventId}");
                return;
            }
            _cacheService.MarkEventAsProcessed(eventWrapper.EventId);
        }

        // Aceita eventos do tipo "message" ou "app_mentions"
        if (evt.Type != "message" && evt.Type != "app_mentions")
            return;

        // Filtra mensagens de bot
        if (evt.Subtype == "bot_message" || evt.BotId != null)
            return;

        // Se não tem usuário (não é uma mensagem válida), ignora
        if (string.IsNullOrEmpty(evt.User))
            return;

        var teamId = eventWrapper.TeamId;
        var token = await _tokenRepository.GetTokenAsync(teamId);
        if (token == null) return;

        // Processa o texto removendo menções ao bot
        var rawText = evt.Text?.Trim() ?? "";
        
        // Remove menções ao bot do texto
        var text = Regex.Replace(rawText, @"<@[^>]+>", "").Trim();
        
        // Verifica se havia menção ao bot no texto original
        var hasMention = Regex.IsMatch(rawText, @"<@[^>]+>");
        
        // Se após remover menções não sobrou nada, ignora
        if (string.IsNullOrEmpty(text))
            return;

        // Verifica se é uma menção ao bot
        var isMention = evt.Type == "app_mentions" || hasMention;
        var startsWithCommand = text.StartsWith("!");
        
        // Usa thread_ts se disponível, senão usa ts
        var threadTs = evt.ThreadTs ?? evt.Ts;
        
        // Verifica se a thread está em modo agente virtual
        // ThreadKey formato: TeamId_UserId_Channel_ThreadTs
        var threadKey = $"{teamId}_{evt.User}_{evt.Channel}_{threadTs}";
        var isInAIMode = _cacheService.IsThreadInAIMode(threadKey);
        
        // Se está em modo agente virtual e é uma mensagem em thread, processa direto com IA
        if (isInAIMode && !string.IsNullOrEmpty(evt.ThreadTs))
        {
            var aiMessage = startsWithCommand ? text.Substring(1).TrimStart() : text;
            
            if (!string.IsNullOrWhiteSpace(aiMessage))
            {
                await _slackService.SendMessageAsync(token.AccessToken, evt.Channel, "🤔 Pensando...", threadTs);
                
                var aiResponse = await _aiService.GetAIResponseAsync(aiMessage, null, threadKey);
                
                if (!string.IsNullOrWhiteSpace(aiResponse))
                {
                    await _slackService.SendMessageAsync(token.AccessToken, evt.Channel, aiResponse, threadTs);
                }
                else
                {
                    await _slackService.SendMessageAsync(
                        token.AccessToken, 
                        evt.Channel, 
                        "⚠️ Não há agentes virtuais disponíveis no momento. Tente novamente em instantes.", 
                        threadTs);
                }
            }
            return;
        }
        
        // Se não for menção e não começar com "!", ignora
        if (!isMention && !startsWithCommand)
            return;

        // Se não começar com "!" mas é uma menção, adiciona para manter compatibilidade
        if (!startsWithCommand && isMention)
            text = "!" + text;

        var spaceIndex = text.IndexOf(' ');
        var commandKey = spaceIndex > 0 ? text[..spaceIndex] : text;
        var args = spaceIndex > 0 ? text[(spaceIndex + 1)..] : "";

        // Tenta executar o comando primeiro
        var slackEvent = evt with { AccessToken = token.AccessToken, TeamId = teamId, Text = text, Ts = threadTs };
        
        // Verifica se é um comando conhecido (simplificado - comandos são tratados no CommandService)
        var knownCommands = new[] { "!ajuda", "!ping", "!horario", "!canal", "!membros", "!lembretes" };
        var isKnownCommand = knownCommands.Any(c => commandKey.Equals(c, StringComparison.OrdinalIgnoreCase));
        
        if (isKnownCommand)
        {
            // Comando encontrado - reseta contador de tentativas e desativa modo agente virtual
            _cacheService.ResetCommandAttempts(threadKey);
            _cacheService.RemovePendingAIMessage(threadKey);
            _cacheService.RemoveThreadAIMode(threadKey);
            _aiService.ClearThreadProvider(threadKey);
            
            await _commandService.ExecuteCommandAsync(commandKey, args, slackEvent, token.AccessToken, teamId);
            return;
        }

        // Se não encontrou comando, incrementa contador de tentativas
        if (isMention || startsWithCommand)
        {
            _cacheService.IncrementCommandAttempts(threadKey);
            var attempts = _cacheService.GetCommandAttempts(threadKey);
            
            var aiMessage = startsWithCommand ? text.Substring(1).TrimStart() : text;
            
            if (!string.IsNullOrWhiteSpace(aiMessage))
            {
                // Se ainda não chegou a 3 tentativas, informa que não encontrou o comando
                if (attempts < 3)
                {
                    await _slackService.SendMessageAsync(
                        token.AccessToken, 
                        evt.Channel, 
                        $"❌ Comando '{commandKey}' não encontrado. Use !ajuda para ver os comandos disponíveis. ({attempts}/3 tentativas)", 
                        threadTs);
                    return;
                }
                
                // Após 3 tentativas, pergunta se quer usar agente virtual
                if (attempts >= 3)
                {
                    _cacheService.SetPendingAIMessage(threadKey, aiMessage, threadTs);
                    
                    var confirmationBlocks = new object[]
                    {
                        new
                        {
                            type = "section",
                            text = new { type = "mrkdwn", text = $"🤖 Não encontrei o comando '{commandKey}' após {attempts} tentativas.\n\nDeseja que eu use um agente virtual para responder sua mensagem?" }
                        },
                        new
                        {
                            type = "actions",
                            elements = new object[]
                            {
                                new 
                                { 
                                    type = "button", 
                                    text = new { type = "plain_text", text = "✅ Sim, usar agente virtual" }, 
                                    action_id = "confirm_ai_yes",
                                    style = "primary",
                                    value = threadKey
                                },
                                new 
                                { 
                                    type = "button", 
                                    text = new { type = "plain_text", text = "❌ Não" }, 
                                    action_id = "confirm_ai_no",
                                    value = threadKey
                                }
                            }
                        }
                    };
                    
                    await _slackService.SendBlocksAsync(token.AccessToken, evt.Channel, confirmationBlocks, threadTs);
                }
            }
        }
    }

    public async Task ProcessInteractiveEventAsync(string payload)
    {
        // Este método será implementado para processar eventos interativos do Slack
        // A lógica completa está no Program.cs e será migrada aqui
        // Por enquanto, deixamos como placeholder
        await Task.CompletedTask;
    }

    public async Task<string?> GetAIResponseForThreadAsync(string userMessage, string threadKey, string? context = null)
    {
        return await _aiService.GetAIResponseAsync(userMessage, context, threadKey);
    }
}

