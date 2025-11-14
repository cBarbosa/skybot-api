namespace skybot.Core.Models.Reminders;

public record ReminderModalSubmission(
    string? TargetUserId,
    string Date,
    string Time,
    string Message
);

