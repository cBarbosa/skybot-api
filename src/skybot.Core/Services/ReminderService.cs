using FluentValidation;
using skybot.Core.Interfaces;
using skybot.Core.Models;
using skybot.Core.Services.Infrastructure;
using skybot.Core.Validators;

namespace skybot.Core.Services;

public class ReminderService : IReminderService
{
    private readonly IReminderRepository _reminderRepository;
    private readonly IValidator<CreateReminderRequest> _validator;

    public ReminderService(IReminderRepository reminderRepository, IValidator<CreateReminderRequest> validator)
    {
        _reminderRepository = reminderRepository;
        _validator = validator;
    }

    public async Task<int> CreateReminderAsync(string teamId, string userId, string message, DateTime dueDate, string? channelId = null)
    {
        var request = new CreateReminderRequest(userId, message, dueDate, channelId);
        
        // Valida usando FluentValidation
        var validationResult = await _validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
            throw new ArgumentException(errors);
        }

        // Assume que req.DueDate vem em horário de Brasília (UTC-3)
        // Converte para UTC antes de salvar
        var dueDateUtc = TimezoneHelper.ConvertToUtc(dueDate);

        // Salva em UTC no banco
        var reminderId = await _reminderRepository.CreateReminderAsync(
            teamId, 
            userId, 
            message, 
            dueDateUtc,  // Salva em UTC
            channelId);

        Console.WriteLine($"[INFO] Lembrete criado: Id={reminderId}, TeamId={teamId}, UserId={userId}, DueDate (BR)={dueDate:dd/MM/yyyy HH:mm}");

        return reminderId;
    }

    public async Task<List<Reminder>> GetRemindersByTeamAsync(string teamId, bool includeSent = false)
    {
        return await _reminderRepository.GetRemindersByTeamAsync(teamId, includeSent);
    }

    public async Task<List<Reminder>> GetRemindersByUserAsync(string teamId, string userId, bool includeSent = false)
    {
        return await _reminderRepository.GetRemindersByUserAsync(teamId, userId, includeSent);
    }

    public async Task ProcessPendingRemindersAsync()
    {
        // Este método será usado pelo ReminderBackgroundService
        // A lógica de processamento será mantida no BackgroundService
        // Este serviço apenas fornece os dados necessários
        var utcNow = DateTime.UtcNow;
        var remindersWithTokens = await _reminderRepository.GetPendingRemindersWithTokensAsync(utcNow);
        
        // O processamento real será feito pelo BackgroundService que tem acesso ao SlackService
        // Este método retorna apenas os dados para processamento
    }
}

