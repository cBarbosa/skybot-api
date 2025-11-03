namespace skybot.Models;

internal record Reminder(
    int Id,
    string? TeamId,
    string? ChannelId,
    string? UserId,
    string? Message,
    DateTime? DueDate,
    bool IsSent = false,
    DateTime? SentAt = null
);

internal record CreateReminderRequest(
    string UserId,
    string Message,
    DateTime DueDate,
    string? ChannelId = null
);

internal record CreateReminderResult(
    bool Success,
    string Message,
    int? ReminderId = null
);

