using skybot.Core.Models;

namespace skybot.Core.Interfaces;

public interface IReminderRepository
{
    Task<int> CreateReminderAsync(string teamId, string userId, string message, DateTime dueDate, string? channelId = null);
    Task<List<Reminder>> GetRemindersByTeamAsync(string teamId, bool includeSent = false);
    Task<List<Reminder>> GetRemindersByUserAsync(string teamId, string userId, bool includeSent = false);
    Task<List<Reminder>> GetPendingRemindersAsync(DateTime utcBefore);
    Task<List<(Reminder Reminder, string? AccessToken)>> GetPendingRemindersWithTokensAsync(DateTime utcBefore);
    Task MarkReminderAsSentAsync(int reminderId);
    Task UpdateReminderTeamIdAsync(int reminderId, string correctTeamId);
}

