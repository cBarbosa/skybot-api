namespace skybot.Services;

internal static class TimezoneHelper
{
    private static readonly TimeZoneInfo BrazilTimeZone = 
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTime GetBrazilianTime()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BrazilTimeZone);
    }

    public static DateTime ConvertToBrazilianTime(DateTime utcTime)
    {
        return TimeZoneInfo.ConvertTimeFromUtc(utcTime, BrazilTimeZone);
    }

    public static DateTime ConvertToUtc(DateTime brazilianTime)
    {
        // Assume que a data recebida está em horário de Brasília
        return TimeZoneInfo.ConvertTimeToUtc(brazilianTime, BrazilTimeZone);
    }
}
