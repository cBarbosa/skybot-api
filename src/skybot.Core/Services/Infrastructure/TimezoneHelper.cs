namespace skybot.Core.Services.Infrastructure;

public static class TimezoneHelper
{
    private static TimeZoneInfo GetBrazilTimeZone()
    {
        try
        {
            // Tenta primeiro com o nome do timezone Linux/Mac
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch
        {
            try
            {
                // Fallback para Windows
                return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
            }
            catch
            {
                // Último fallback: UTC-3 manual
                return TimeZoneInfo.CreateCustomTimeZone(
                    "Brazil/East",
                    TimeSpan.FromHours(-3),
                    "Brasília",
                    "Brasília");
            }
        }
    }

    private static readonly TimeZoneInfo BrazilTimeZone = GetBrazilTimeZone();

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

