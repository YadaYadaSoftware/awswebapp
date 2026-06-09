using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Migrate-on-startup helper for the Tjb web framework.
/// </summary>
public static class MigrationApplicationExtensions
{
    private const string LogCategory = "Tjb.Web.Hosting.Migrations";

    /// <summary>
    /// Ensures the database exists and applies pending migrations for <typeparamref name="TContext"/>.
    /// Failures are logged and swallowed so the application still starts (matching the prior
    /// non-throwing migrate-on-startup behavior — important for container cold-start).
    /// </summary>
    public static async Task ApplyDatabaseMigrationsAsync<TContext>(this WebApplication app)
        where TContext : DbContext
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);

        try
        {
            logger.LogInformation("Ensuring database exists and applying migrations for {Context}...", typeof(TContext).Name);
            var context = services.GetRequiredService<TContext>();

            // This will create the database if it doesn't exist
            await context.Database.EnsureCreatedAsync();

            // This will apply all pending migrations
            await context.Database.MigrateAsync();

            logger.LogInformation("{Context} migrations applied successfully.", typeof(TContext).Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying database migrations.");
            // Don't throw - let the application start even if migrations fail
            // This prevents application startup issues in production
        }
    }
}
