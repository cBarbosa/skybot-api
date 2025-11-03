using skybot.Repositories;
using skybot.Services;

namespace skybot.Services;

public class ReminderBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(10); // Verifica a cada 10 minutos

    public ReminderBackgroundService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
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
        var reminderRepository = scope.ServiceProvider.GetRequiredService<ReminderRepository>();
        var slackService = scope.ServiceProvider.GetRequiredService<SlackService>();
        var tokenRepository = scope.ServiceProvider.GetRequiredService<SlackTokenRepository>();

        // Usa hora atual em UTC para buscar no banco (que está em UTC)
        var utcNow = DateTime.UtcNow;
        
        // Busca lembretes onde DueDate (em UTC) <= agora (UTC)
        var reminders = await reminderRepository.GetPendingRemindersAsync(utcNow);

        if (reminders.Count == 0)
            return;

        Console.WriteLine($"[INFO] Processando {reminders.Count} lembrete(s) pendente(s)...");

        foreach (var reminder in reminders)
        {
            try
            {
                // Busca o token do team
                var token = await tokenRepository.GetTokenAsync(reminder.TeamId!);
                if (token == null)
                {
                    Console.WriteLine($"[WARNING] Token não encontrado para team {reminder.TeamId}");
                    continue;
                }

                // Converte DueDate de UTC para horário de Brasília para exibição
                var dueDateBr = reminder.DueDate.HasValue 
                    ? TimezoneHelper.ConvertToBrazilianTime(reminder.DueDate.Value)
                    : TimezoneHelper.GetBrazilianTime();

                // Determina onde enviar: ChannelId ou UserId (DM)
                var channel = reminder.ChannelId ?? reminder.UserId!;
                var message = $"🔔 *Lembrete:* {reminder.Message}";

                // Envia mensagem via Slack
                var sent = await slackService.SendMessageAsync(token.AccessToken, channel, message);

                if (sent)
                {
                    // Marca como enviado (mantém histórico no banco)
                    await reminderRepository.MarkReminderAsSentAsync(reminder.Id);
                    Console.WriteLine($"[INFO] Lembrete {reminder.Id} enviado com sucesso para {channel} (hora original: {dueDateBr:dd/MM/yyyy HH:mm} BR)");
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
