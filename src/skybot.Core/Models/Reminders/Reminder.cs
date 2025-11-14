namespace skybot.Core.Models;

public record Reminder(
    int Id,
    string? TeamId,
    string? ChannelId,
    string? UserId,
    string? Message,
    DateTime? DueDate,
    bool IsSent = false,
    DateTime? SentAt = null
);

public record CreateReminderRequest(
    string UserId,
    string Message,
    DateTime DueDate,
    string? ChannelId = null
);

public record CreateReminderResult(
    bool Success,
    string Message,
    int? ReminderId = null
);

