using FluentValidation;
using skybot.Core.Models.Reminders;
using skybot.Core.Services.Infrastructure;

namespace skybot.Core.Validators;

public class ReminderModalSubmissionValidator : AbstractValidator<ReminderModalSubmission>
{
    public ReminderModalSubmissionValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty()
            .WithMessage("A mensagem é obrigatória.")
            .MaximumLength(1000)
            .WithMessage("A mensagem não pode exceder 1000 caracteres.");

        RuleFor(x => x.Time)
            .NotEmpty()
            .WithMessage("A hora é obrigatória.")
            .Must(BeValidTimeFormat)
            .WithMessage("Formato de hora inválido. Use HH:mm");

        RuleFor(x => x.Date)
            .NotEmpty()
            .WithMessage("A data é obrigatória.")
            .Must(BeValidDateFormat)
            .WithMessage("Formato de data inválido. Use YYYY-MM-DD");

        RuleFor(x => x)
            .Must(BeFutureDateTime)
            .WithMessage("A data/hora deve ser no futuro.")
            .When(x => !string.IsNullOrEmpty(x.Date) && !string.IsNullOrEmpty(x.Time));

        // TargetUserId é opcional - só é obrigatório quando enviando para outra pessoa
        // Mas isso é validado no serviço, não aqui
    }

    private bool BeValidTimeFormat(string? time)
    {
        if (string.IsNullOrWhiteSpace(time))
            return false;

        return TimeSpan.TryParse(time, out _);
    }

    private bool BeValidDateFormat(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
            return false;

        return DateTime.TryParse(date, out _);
    }

    private bool BeFutureDateTime(ReminderModalSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.Date) || string.IsNullOrWhiteSpace(submission.Time))
            return false;

        if (!TimeSpan.TryParse(submission.Time, out var timeSpan))
            return false;

        if (!DateTime.TryParse(submission.Date, out var date))
            return false;

        var dueDate = new DateTime(date.Year, date.Month, date.Day, timeSpan.Hours, timeSpan.Minutes, 0);
        var dueDateBr = TimezoneHelper.GetBrazilianTime();

        return dueDate > dueDateBr;
    }
}

