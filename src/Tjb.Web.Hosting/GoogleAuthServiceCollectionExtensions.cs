using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Google OAuth wiring for the Tjb web framework, preserving the ticket/failure event logging
/// and the OAuth-callback database connectivity diagnostic.
/// </summary>
public static class GoogleAuthServiceCollectionExtensions
{
    private const string LogCategory = "Tjb.Web.Hosting.GoogleAuth";

    /// <summary>
    /// Adds authentication with Google OAuth, reading <c>Authentication:Google:ClientId</c> and
    /// <c>Authentication:Google:ClientSecret</c> from configuration. The <see cref="OAuthEvents.OnCreatingTicket"/>
    /// handler runs a connectivity check against <typeparamref name="TContext"/> for diagnostics, matching the
    /// prior inline wiring.
    /// </summary>
    public static AuthenticationBuilder AddAwsWebAppGoogleAuth<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : DbContext
    {
        return services.AddAuthentication().AddGoogle(googleOptions =>
        {
            googleOptions.ClientId = configuration["Authentication:Google:ClientId"]!;
            googleOptions.ClientSecret = configuration["Authentication:Google:ClientSecret"]!;

            googleOptions.Events.OnCreatingTicket = async context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
                logger.LogInformation("Google OAuth creating ticket for user: {User}", context.Identity?.Name ?? "Unknown");

                try
                {
                    // Test database connectivity
                    var dbContext = context.HttpContext.RequestServices.GetRequiredService<TContext>();
                    var canConnect = await dbContext.Database.CanConnectAsync();
                    logger.LogInformation("Database connectivity check: {CanConnect}", canConnect);

                    if (!canConnect)
                    {
                        logger.LogError("Database is not accessible during OAuth callback");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during database connectivity check in OAuth");
                }

                await Task.CompletedTask;
            };

            googleOptions.Events.OnRemoteFailure = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
                logger.LogError(context.Failure, "Google OAuth remote failure");
                return Task.CompletedTask;
            };

            googleOptions.Events.OnTicketReceived = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
                logger.LogInformation("Google OAuth ticket received");
                return Task.CompletedTask;
            };
        });
    }

    /// <summary>
    /// Logs whether the Google OAuth ClientId/ClientSecret are configured, matching the prior
    /// startup diagnostic block.
    /// </summary>
    public static WebApplication LogAwsWebAppAuthConfig(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var clientId = configuration["Authentication:Google:ClientId"];
        var clientSecret = configuration["Authentication:Google:ClientSecret"];
        logger.LogInformation("Google OAuth ClientId configured: {Configured}", !string.IsNullOrEmpty(clientId));
        logger.LogInformation("Google OAuth ClientSecret configured: {Configured}", !string.IsNullOrEmpty(clientSecret));
        return app;
    }
}
