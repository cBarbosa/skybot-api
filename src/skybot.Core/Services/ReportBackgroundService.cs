using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using skybot.Core.Interfaces;
using skybot.Core.Models.Slack;

namespace skybot.Core.Services;

public class ReportBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(10); // Verifica a cada 10 minutos
    private DateTime _lastCheck = DateTime.MinValue;

    public ReportBackgroundService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[INFO] ReportBackgroundService iniciado (relatórios desabilitados por padrão)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndSendReportsAsync();
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Erro no ReportBackgroundService: {ex.Message}");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task CheckAndSendReportsAsync()
    {
        var now = DateTime.Now;
        var today = now.Date;

        // Evita múltiplas verificações no mesmo dia
        if (_lastCheck.Date == today)
            return;

        using var scope = _serviceProvider.CreateScope();
        var workspaceSettingsRepo = scope.ServiceProvider.GetRequiredService<IWorkspaceSettingsRepository>();
        var slackService = scope.ServiceProvider.GetRequiredService<ISlackService>();
        var messageLogRepo = scope.ServiceProvider.GetRequiredService<IMessageLogRepository>();

        // Obter todas as configurações de workspace
        var allSettings = await workspaceSettingsRepo.GetAllAsync();

        foreach (var settings in allSettings)
        {
            try
            {
                // Relatório Diário
                if (settings.DailyReportEnabled && ShouldSendReport(now, settings.DailyReportTime))
                {
                    await SendDailyReportAsync(settings.TeamId, settings.AdminUserId, slackService, messageLogRepo);
                }

                // Relatório Semanal
                if (settings.WeeklyReportEnabled && now.DayOfWeek == (DayOfWeek)settings.WeeklyReportDay 
                    && ShouldSendReport(now, settings.WeeklyReportTime))
                {
                    await SendWeeklyReportAsync(settings.TeamId, settings.AdminUserId, slackService, messageLogRepo);
                }

                // Relatório Mensal
                if (settings.MonthlyReportEnabled && now.Day == settings.MonthlyReportDay 
                    && ShouldSendReport(now, settings.MonthlyReportTime))
                {
                    await SendMonthlyReportAsync(settings.TeamId, settings.AdminUserId, slackService, messageLogRepo);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Erro ao enviar relatório para team {settings.TeamId}: {ex.Message}");
            }
        }

        _lastCheck = today;
    }

    private bool ShouldSendReport(DateTime now, TimeSpan targetTime)
    {
        var currentTime = now.TimeOfDay;
        // Envia se estiver dentro da janela de 10 minutos após o horário configurado
        return currentTime >= targetTime && currentTime < targetTime.Add(TimeSpan.FromMinutes(10));
    }

    private async Task SendDailyReportAsync(string teamId, string adminUserId, ISlackService slackService, IMessageLogRepository messageLogRepo)
    {
        var yesterday = DateTime.UtcNow.AddDays(-1).Date;
        var stats = await messageLogRepo.GetDailyStatsAsync(teamId, yesterday);
        var total = stats.Values.Sum();

        var blocks = new List<object>
        {
            new
            {
                type = "header",
                text = new { type = "plain_text", text = $"📊 Relatório Diário - {yesterday:dd/MM/yyyy}" }
            },
            new
            {
                type = "section",
                text = new
                {
                    type = "mrkdwn",
                    text = $"*Total de mensagens enviadas:* {total}\n\n" +
                           $"• Canais: {stats.GetValueOrDefault("CHANNEL", 0)}\n" +
                           $"• Usuários (DM): {stats.GetValueOrDefault("USER", 0)}\n" +
                           $"• Grupos: {stats.GetValueOrDefault("GROUP", 0)}"
                }
            },
            new { type = "divider" },
            new
            {
                type = "context",
                elements = new[]
                {
                    new { type = "mrkdwn", text = $"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm}" }
                }
            }
        };

        var request = new SendMessageRequest(
            DestinationType: DestinationType.USER,
            DestinationId: adminUserId,
            Text: null,
            Blocks: blocks,
            ThreadTs: null
        );

        var result = await slackService.SendMessageAsync(teamId, request);
        
        if (result.Success)
        {
            Console.WriteLine($"[INFO] Relatório diário enviado para admin do team {teamId}");
        }
        else
        {
            Console.WriteLine($"[ERROR] Falha ao enviar relatório diário: {result.Error}");
        }
    }

    private async Task SendWeeklyReportAsync(string teamId, string adminUserId, ISlackService slackService, IMessageLogRepository messageLogRepo)
    {
        var lastWeek = DateTime.UtcNow.AddDays(-7);
        var stats = await messageLogRepo.GetWeeklyStatsAsync(teamId, lastWeek);
        var total = stats.Values.Sum();

        var statsText = string.Join("\n", stats.Select(kvp => $"• {kvp.Key}: {kvp.Value} mensagens"));

        var blocks = new List<object>
        {
            new
            {
                type = "header",
                text = new { type = "plain_text", text = "📈 Relatório Semanal" }
            },
            new
            {
                type = "section",
                text = new
                {
                    type = "mrkdwn",
                    text = $"*Período:* {lastWeek:dd/MM} a {DateTime.Now:dd/MM/yyyy}\n" +
                           $"*Total:* {total} mensagens\n\n" +
                           statsText
                }
            },
            new { type = "divider" },
            new
            {
                type = "context",
                elements = new[]
                {
                    new { type = "mrkdwn", text = $"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm}" }
                }
            }
        };

        var request = new SendMessageRequest(
            DestinationType: DestinationType.USER,
            DestinationId: adminUserId,
            Text: null,
            Blocks: blocks,
            ThreadTs: null
        );

        await slackService.SendMessageAsync(teamId, request);
        Console.WriteLine($"[INFO] Relatório semanal enviado para admin do team {teamId}");
    }

    private async Task SendMonthlyReportAsync(string teamId, string adminUserId, ISlackService slackService, IMessageLogRepository messageLogRepo)
    {
        var lastMonth = DateTime.UtcNow.AddMonths(-1);
        var stats = await messageLogRepo.GetMonthlyStatsAsync(teamId, lastMonth.Year, lastMonth.Month);
        var total = stats.Values.Sum();

        var blocks = new List<object>
        {
            new
            {
                type = "header",
                text = new { type = "plain_text", text = $"📅 Relatório Mensal - {lastMonth:MMMM/yyyy}" }
            },
            new
            {
                type = "section",
                text = new
                {
                    type = "mrkdwn",
                    text = $"*Total de mensagens:* {total}\n\n" +
                           $"Média diária: {(stats.Count > 0 ? total / stats.Count : 0)} mensagens"
                }
            },
            new { type = "divider" },
            new
            {
                type = "context",
                elements = new[]
                {
                    new { type = "mrkdwn", text = $"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm}" }
                }
            }
        };

        var request = new SendMessageRequest(
            DestinationType: DestinationType.USER,
            DestinationId: adminUserId,
            Text: null,
            Blocks: blocks,
            ThreadTs: null
        );

        await slackService.SendMessageAsync(teamId, request);
        Console.WriteLine($"[INFO] Relatório mensal enviado para admin do team {teamId}");
    }
}

