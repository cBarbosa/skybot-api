namespace skybot.Core.Models.Settings;

public record WorkspaceSettings(
    string TeamId,
    string AdminUserId,
    bool DailyReportEnabled,
    TimeSpan DailyReportTime,
    bool WeeklyReportEnabled,
    sbyte WeeklyReportDay,
    TimeSpan WeeklyReportTime,
    bool MonthlyReportEnabled,
    sbyte MonthlyReportDay,
    TimeSpan MonthlyReportTime,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

// Extension methods para facilitar o uso
public static class WorkspaceSettingsExtensions
{
    public static TimeOnly GetDailyReportTimeOnly(this WorkspaceSettings settings) 
        => TimeOnly.FromTimeSpan(settings.DailyReportTime);
    
    public static TimeOnly GetWeeklyReportTimeOnly(this WorkspaceSettings settings) 
        => TimeOnly.FromTimeSpan(settings.WeeklyReportTime);
    
    public static TimeOnly GetMonthlyReportTimeOnly(this WorkspaceSettings settings) 
        => TimeOnly.FromTimeSpan(settings.MonthlyReportTime);
}

