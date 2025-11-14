using skybot.Core.Interfaces;

namespace skybot.API.Middleware;

public class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

    // Endpoints públicos que não precisam de autenticação
    private static readonly HashSet<string> PublicEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/health/liveness",
        "/slack/install",
        "/slack/oauth",
        "/slack/events",
        "/slack/interactive"
    };

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IApiKeyRepository apiKeyRepository)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Endpoints públicos passam direto
        var isPublic = path == "/" || PublicEndpoints.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        
        if (isPublic)
        {
            await _next(context);
            return;
        }

        // Verifica se tem API Key no header
        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyValue) || 
            string.IsNullOrEmpty(apiKeyValue))
        {
            _logger.LogWarning("❌ [DEBUG] API Key não enviada");
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new 
            { 
                success = false, 
                message = "API Key obrigatória. Use o header 'X-Api-Key'." 
            });
            return;
        }

        // Valida a API Key
        var apiKey = await apiKeyRepository.GetByKeyAsync(apiKeyValue!);
        
        if (apiKey == null)
        {
            _logger.LogWarning("❌ [DEBUG] API Key inválida: {Key}", apiKeyValue!);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new 
            { 
                success = false, 
                message = "API Key inválida." 
            });
            return;
        }

        if (!apiKey.IsActive)
        {
            _logger.LogWarning("❌ [DEBUG] API Key revogada: {Key}", apiKeyValue!);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new 
            { 
                success = false, 
                message = "API Key revogada." 
            });
            return;
        }

        if (apiKey.ExpiresAt.HasValue && apiKey.ExpiresAt.Value < DateTime.UtcNow)
        {
            _logger.LogWarning("❌ [DEBUG] API Key expirada: {Key}", apiKeyValue!);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new 
            { 
                success = false, 
                message = "API Key expirada." 
            });
            return;
        }

        // Verifica se o endpoint é permitido (se houver restrição)
        if (apiKey.AllowedEndpoints != null && apiKey.AllowedEndpoints.Count > 0)
        {
            string.Join(", ", apiKey.AllowedEndpoints);

            var isAllowed = apiKey.AllowedEndpoints.Any(e => 
                path.StartsWith(e, StringComparison.OrdinalIgnoreCase));
            
            if (!isAllowed)
            {
                _logger.LogWarning("❌ [DEBUG] Endpoint não autorizado para esta API Key: {Path}", path);
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new 
                { 
                    success = false, 
                    message = "Endpoint não autorizado para esta API Key." 
                });
                return;
            }
        }

        // Injeta o TeamId no contexto para uso nos endpoints
        context.Items["TeamId"] = apiKey.TeamId;
        context.Items["ApiKeyName"] = apiKey.Name;

        // Atualiza LastUsedAt em background (não bloqueia request)
        _ = Task.Run(() => apiKeyRepository.UpdateLastUsedAsync(apiKeyValue!));

        await _next(context);
    }
}

