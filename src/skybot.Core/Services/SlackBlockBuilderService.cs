using skybot.Core.Interfaces;
using skybot.Core.Services.Infrastructure;

namespace skybot.Core.Services;

public class SlackBlockBuilderService : ISlackBlockBuilderService
{
    public object[] CreateRemindersMenuBlocks(string userId) => new object[]
    {
        new
        {
            type = "section",
            text = new { type = "mrkdwn", text = "🔔 *Gerenciar Lembretes*\nEscolha uma opção:" }
        },
        new
        {
            type = "actions",
            elements = new[]
            {
                new { type = "button", text = new { type = "plain_text", text = "📋 Ver Meus Lembretes" }, action_id = "view_my_reminders", value = userId },
                new { type = "button", text = new { type = "plain_text", text = "➕ Adicionar Lembrete" }, action_id = "add_reminder_modal", value = userId },
                new { type = "button", text = new { type = "plain_text", text = "📤 Enviar para Alguém" }, action_id = "send_reminder_modal", value = userId }
            }
        }
    };

    public object CreateReminderModal(bool isForSomeone, string triggerId)
    {
        var now = TimezoneHelper.GetBrazilianTime();
        var defaultDate = now.AddHours(1).ToString("yyyy-MM-dd");
        var defaultTime = now.AddHours(1).ToString("HH:mm");

        var modalBlocks = new List<object>();

        if (isForSomeone)
        {
            modalBlocks.Add(new
            {
                type = "input",
                block_id = "user_select",
                label = new { type = "plain_text", text = "Enviar para" },
                element = new
                {
                    type = "users_select",
                    action_id = "user",
                    placeholder = new { type = "plain_text", text = "Selecione um usuário" }
                }
            });
        }

        modalBlocks.Add(new
        {
            type = "input",
            block_id = "date_input",
            label = new { type = "plain_text", text = "Data" },
            element = new
            {
                type = "datepicker",
                action_id = "date",
                initial_date = defaultDate,
                placeholder = new { type = "plain_text", text = "Selecione a data" }
            }
        });

        modalBlocks.Add(new
        {
            type = "input",
            block_id = "time_input",
            label = new { type = "plain_text", text = "Hora (HH:mm)" },
            element = new
            {
                type = "plain_text_input",
                action_id = "time",
                initial_value = defaultTime,
                placeholder = new { type = "plain_text", text = "14:30" }
            }
        });

        modalBlocks.Add(new
        {
            type = "input",
            block_id = "message_input",
            label = new { type = "plain_text", text = "Mensagem" },
            element = new
            {
                type = "plain_text_input",
                action_id = "message",
                multiline = true,
                placeholder = new { type = "plain_text", text = "Digite a mensagem do lembrete..." }
            },
            optional = false
        });

        return new
        {
            type = "modal",
            callback_id = isForSomeone ? "send_reminder_submit" : "add_reminder_submit",
            title = new { type = "plain_text", text = isForSomeone ? "Enviar Lembrete" : "Criar Lembrete" },
            submit = new { type = "plain_text", text = "Criar" },
            close = new { type = "plain_text", text = "Cancelar" },
            blocks = modalBlocks
        };
    }
}

