using FluentValidation;
using skybot.Core.Models.Slack;

namespace skybot.Core.Validators;

public class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.DestinationType)
            .IsInEnum()
            .WithMessage("DestinationType deve ser CHANNEL, USER ou GROUP");

        RuleFor(x => x.DestinationId)
            .NotEmpty()
            .WithMessage("DestinationId é obrigatório");

        RuleFor(x => x.DestinationId)
            .Must((request, destinationId) => ValidateDestinationId(request.DestinationType, destinationId))
            .WithMessage(request => $"DestinationId inválido para o tipo {request.DestinationType}. Esperado formato: {GetExpectedFormat(request.DestinationType)}");

        // Validação: deve ter Text OU Blocks, mas não ambos e não nenhum
        RuleFor(x => x)
            .Must(x => (x.Text != null && x.Blocks == null) || (x.Text == null && x.Blocks != null))
            .WithMessage("Forneça apenas 'text' (mensagem simples) OU 'blocks' (Block Kit), não ambos");

        RuleFor(x => x.Text)
            .MaximumLength(4000)
            .When(x => x.Text != null)
            .WithMessage("Text não pode ter mais de 4000 caracteres");

        RuleFor(x => x.Blocks)
            .Must(blocks => blocks != null && blocks.Count > 0)
            .When(x => x.Blocks != null)
            .WithMessage("Blocks não pode ser vazio");
    }

    private bool ValidateDestinationId(DestinationType type, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        return type switch
        {
            DestinationType.CHANNEL => id.StartsWith("C"),
            DestinationType.USER => id.StartsWith("U"),
            DestinationType.GROUP => id.StartsWith("G"),
            _ => false
        };
    }

    private string GetExpectedFormat(DestinationType type)
    {
        return type switch
        {
            DestinationType.CHANNEL => "C1234567890",
            DestinationType.USER => "U1234567890",
            DestinationType.GROUP => "G1234567890",
            _ => "ID inválido"
        };
    }
}

