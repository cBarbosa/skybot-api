using FluentValidation;
using skybot.Core.Models;

namespace skybot.Core.Validators;

public class CreateChannelRequestValidator : AbstractValidator<CreateChannelRequest>
{
    public CreateChannelRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Nome do canal é obrigatório.")
            .MinimumLength(1)
            .WithMessage("Nome do canal deve ter pelo menos 1 caractere.")
            .MaximumLength(80)
            .WithMessage("Nome do canal não pode exceder 80 caracteres.")
            .Matches(@"^[a-z0-9-_]+$")
            .WithMessage("Nome do canal deve conter apenas letras minúsculas, números, hífens e underscores.");
    }
}

