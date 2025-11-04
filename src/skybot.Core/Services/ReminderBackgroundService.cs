using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using skybot.Core.Interfaces;
using skybot.Core.Models;
using skybot.Core.Services.Infrastructure;

namespace skybot.Core.Services;

public class ReminderBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _checkInterval;

    public ReminderBackgroundService(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        var intervalMinutes = configuration.GetValue<int>("ReminderBackgroundService:CheckIntervalMinutes", 10);
        _checkInterval = TimeSpan.FromMinutes(intervalMinutes);
        Console.WriteLine($"[INFO] ReminderBackgroundService configurado com intervalo de {intervalMinutes} minutos");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingRemindersAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Erro no background service de lembretes: {ex.Message}");
                Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingRemindersAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var reminderRepository = scope.ServiceProvider.GetRequiredService<IReminderRepository>();
        var slackService = scope.ServiceProvider.GetRequiredService<ISlackService>();
        var tokenRepository = scope.ServiceProvider.GetRequiredService<ISlackTokenRepository>();

        // Usa hora atual em UTC para buscar no banco (que está em UTC)
        var utcNow = DateTime.UtcNow;
        
        // Busca lembretes com AccessToken em uma única query usando JOIN (otimização)
        var remindersWithTokens = await reminderRepository.GetPendingRemindersWithTokensAsync(utcNow);

        if (remindersWithTokens.Count == 0)
            return;

        Console.WriteLine($"[INFO] Processando {remindersWithTokens.Count} lembrete(s) pendente(s)...");

        // Busca todos os tokens apenas se precisar fazer fallback (lazy loading)
        List<SlackToken>? allTokensCache = null;

        foreach (var item in remindersWithTokens)
        {
            var reminder = item.Reminder;
            var accessToken = item.AccessToken;
            try
            {
                string? tokenToUse = null;
                string? correctTeamId = null;

                // Se o AccessToken já veio do JOIN, usa ele diretamente
                if (!string.IsNullOrEmpty(accessToken))
                {
                    tokenToUse = accessToken;
                    correctTeamId = reminder.TeamId;
                }

                // Se não encontrou AccessToken no JOIN, tenta descobrir o TeamId correto (fallback)
                if (string.IsNullOrEmpty(tokenToUse))
                {
                    // Busca todos os tokens apenas uma vez, se necessário (lazy loading)
                    allTokensCache ??= await tokenRepository.GetAllTokensAsync();

                    // Tenta descobrir o TeamId usando os tokens disponíveis
                    foreach (var testToken in allTokensCache)
                    {
                        correctTeamId = await slackService.GetTeamIdFromTokenAsync(testToken.AccessToken);

                        if (correctTeamId is not null)
                        {
                            // Busca o token correto pelo TeamId descoberto
                            var foundToken = await tokenRepository.GetTokenAsync(correctTeamId);

                            if (foundToken is not null)
                            {
                                tokenToUse = foundToken.AccessToken;
                                
                                // Se encontrou e o TeamId é diferente, atualiza o lembrete
                                if (!string.IsNullOrEmpty(reminder.TeamId) && reminder.TeamId != correctTeamId)
                                {
                                    await reminderRepository.UpdateReminderTeamIdAsync(reminder.Id, correctTeamId);
                                    Console.WriteLine($"[INFO] TeamId do lembrete {reminder.Id} atualizado: '{reminder.TeamId}' -> '{correctTeamId}'");
                                }
                                break; // Encontrou um token válido, sai do loop
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(tokenToUse))
                {
                    Console.WriteLine($"[WARNING] Não foi possível encontrar token para o lembrete {reminder.Id}. TeamId: '{reminder.TeamId}', ChannelId: '{reminder.ChannelId}', UserId: '{reminder.UserId}'");
                    continue;
                }

                // Converte DueDate de UTC para horário de Brasília para exibição
                var dueDateBr = reminder.DueDate.HasValue 
                    ? TimezoneHelper.ConvertToBrazilianTime(reminder.DueDate.Value)
                    : TimezoneHelper.GetBrazilianTime();

                // Determina onde enviar: ChannelId ou UserId (DM)
                var channel = reminder.ChannelId ?? reminder.UserId!;
                var message = $"🔔 *Lembrete:* {reminder.Message}";

                // Envia mensagem via Slack (tokenToUse não pode ser null aqui devido à verificação acima)
                var sent = await slackService.SendMessageAsync(tokenToUse, channel, message);

                if (sent)
                {
                    // Marca como enviado (mantém histórico no banco)
                    await reminderRepository.MarkReminderAsSentAsync(reminder.Id);
                    Console.WriteLine($"[INFO] Lembrete {reminder.Id} enviado com sucesso para {channel} (TeamId: {correctTeamId ?? reminder.TeamId}, hora original: {dueDateBr:dd/MM/yyyy HH:mm} BR)");
                }
                else
                {
                    Console.WriteLine($"[WARNING] Falha ao enviar lembrete {reminder.Id}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Erro ao processar lembrete {reminder.Id}: {ex.Message}");
                Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
            }
        }
    }
}


