using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using skybot.Core.Interfaces;
using skybot.Core.Providers;
using skybot.Core.Repositories;
using skybot.Core.Services;
using skybot.Core.Services.Infrastructure;
using skybot.Core.Validators;

namespace skybot.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Adiciona serviços do Core
        services.AddCoreServices(configuration);

        // Adiciona HttpClient
        services.AddHttpClient();

        // Valida configurações obrigatórias
        var requiredConfigs = new[]
        {
            ("Slack:ClientId", configuration["Slack:ClientId"]),
            ("Slack:ClientSecret", configuration["Slack:ClientSecret"]),
            ("Slack:RedirectUri", configuration["Slack:RedirectUri"]),
            ("Slack:Scopes", configuration["Slack:Scopes"]),
            ("ConnectionStrings:MySqlConnection", configuration.GetConnectionString("MySqlConnection"))
        };

        var missingConfigs = requiredConfigs.Where(c => string.IsNullOrWhiteSpace(c.Item2)).ToList();
        if (missingConfigs.Any())
        {
            throw new InvalidOperationException(
                $"Configurações obrigatórias não encontradas: {string.Join(", ", missingConfigs.Select(c => c.Item1))}");
        }

        return services;
    }

    public static IServiceCollection AddApiCors(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("SkybotPolicy", policy =>
            {
                if (environment.IsDevelopment())
                {
                    // Desenvolvimento: permissivo
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                }
                else
                {
                    // Produção: restrito
                    policy.WithOrigins("https://slack.com", "https://hooks.slack.com")
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                }
            });
        });

        return services;
    }

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["liveness"])
            .AddMySql(configuration.GetConnectionString("MySqlConnection") ?? throw new InvalidOperationException("Connection string não configurada"));

        return services;
    }

    public static IServiceCollection AddApiSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Skybot API",
                Version = "1.0.0",
                Description = "API para integração do Skybot com Slack"
            });
        });

        return services;
    }

    public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Repositórios - Scoped (seguro para Dapper)
        services.AddScoped<IReminderRepository, ReminderRepository>();
        services.AddScoped<ISlackTokenRepository, SlackTokenRepository>();
        services.AddScoped<ITokenRefreshHistoryRepository, TokenRefreshHistoryRepository>();

        // Serviços de infraestrutura - Scoped
        services.AddScoped<ISlackService, SlackService>();
        services.AddScoped<ISlackTokenRefreshService, SlackTokenRefreshService>();
        services.AddScoped<IAIService, AIService>();
        services.AddSingleton<ICacheService, CacheService>(); // Singleton para cache em memória

        // Providers de IA - Transient (criados a cada uso)
        services.AddTransient<IAIProvider, OpenAIProvider>();
        services.AddTransient<IAIProvider, GeminiProvider>();

        // Serviços de aplicação (regras de negócio) - Scoped
        services.AddScoped<ICommandService, CommandService>();
        services.AddScoped<IReminderService, ReminderService>();
        services.AddScoped<ISlackIntegrationService, SlackIntegrationService>();
        services.AddScoped<ISlackInteractiveService, SlackInteractiveService>();
        services.AddScoped<ISlackBlockBuilderService, SlackBlockBuilderService>();

        // Background Service
        services.AddHostedService<ReminderBackgroundService>();

        // Validators
        services.AddScoped<IValidator<skybot.Core.Models.CreateReminderRequest>, CreateReminderRequestValidator>();
        services.AddScoped<IValidator<skybot.Core.Models.CreateChannelRequest>, CreateChannelRequestValidator>();
        services.AddScoped<IValidator<skybot.Core.Models.Reminders.ReminderModalSubmission>, ReminderModalSubmissionValidator>();

        return services;
    }
}

