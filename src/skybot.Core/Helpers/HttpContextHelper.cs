using Microsoft.AspNetCore.Http;

namespace skybot.Core.Helpers;

public static class HttpContextHelper
{
    public static string? GetSourceIp(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString();
    }

    public static string? GetForwardedFor(HttpContext context)
    {
        // Verifica múltiplos headers de proxy
        return context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? context.Request.Headers["X-Real-IP"].FirstOrDefault()
            ?? context.Request.Headers["CF-Connecting-IP"].FirstOrDefault(); // Cloudflare
    }

    public static string? GetUserAgent(HttpContext context)
    {
        return context.Request.Headers["User-Agent"].FirstOrDefault();
    }

    public static string? GetReferer(HttpContext context)
    {
        return context.Request.Headers["Referer"].FirstOrDefault();
    }

    public static string GenerateRequestId()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static string? GetRealClientIp(HttpContext context)
    {
        // Prioriza o IP real de trás de proxy, senão usa o IP direto
        return GetForwardedFor(context) ?? GetSourceIp(context);
    }
}

