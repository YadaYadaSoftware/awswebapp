using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sample.Data;

namespace Sample.Migrations;

/// <summary>
/// Standalone migrate runner for the sample (mirrors Tjb.Migrations). Applies pending migrations
/// for <see cref="SampleDbContext"/>; the Guestbook domain needs no seed (entries are user-created).
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();

        try
        {
            var context = services.GetRequiredService<SampleDbContext>();
            logger.LogInformation("Applying Sample database migrations...");
            await context.Database.MigrateAsync();
            logger.LogInformation("Sample database migrations completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while migrating the Sample database.");
            throw;
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                var connectionString = context.Configuration.GetConnectionString("DefaultConnection");

                services.AddDbContext<SampleDbContext>(options =>
                    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
                        mysqlOptions => mysqlOptions.MigrationsAssembly("Sample.Migrations")));

                services.AddLogging();
            });
}
