using FluentValidation;
using skybot.Core.Models;
using skybot.Core.Services.Infrastructure;

namespace skybot.Core.Validators;

public class CreateReminderRequestValidator : AbstractValidator<CreateReminderRequest>
{
    public CreateReminderRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId é obrigatório.");

        RuleFor(x => x.Message)
            .NotEmpty()
            .WithMessage("Message é obrigatória.")
            .MaximumLength(1000)
            .WithMessage("Message não pode exceder 1000 caracteres.");

        RuleFor(x => x.DueDate)
            .Must(BeFutureDate)
            .WithMessage("DueDate deve ser uma data futura (horário de Brasília).");

        RuleFor(x => x.ChannelId)
            .MaximumLength(50)
            .WithMessage("ChannelId não pode exceder 50 caracteres.")
            .When(x => !string.IsNullOrEmpty(x.ChannelId));
    }

    private bool BeFutureDate(DateTime date)
    {
        // Converte para UTC para comparação
        var dueDateUtc = TimezoneHelper.ConvertToUtc(date);
        return dueDateUtc > DateTime.UtcNow;
    }
}

