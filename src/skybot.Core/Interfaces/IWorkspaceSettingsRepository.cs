using skybot.Core.Models.Settings;

namespace skybot.Core.Interfaces;

public interface IWorkspaceSettingsRepository
{
    Task<WorkspaceSettings?> GetByTeamIdAsync(string teamId);
    Task<List<WorkspaceSettings>> GetAllAsync();
    Task CreateAsync(string teamId, string adminUserId);
    Task UpdateReportSettingsAsync(string teamId, bool? dailyEnabled = null, bool? weeklyEnabled = null, bool? monthlyEnabled = null);
}

