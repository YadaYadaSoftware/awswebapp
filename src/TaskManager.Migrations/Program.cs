using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pomelo.EntityFrameworkCore.MySql;
using TaskManager.Data;

namespace TaskManager.Migrations;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        var logger = services.GetRequiredService<ILogger<Program>>();

        logger.LogWarning("Manual migration application is disabled.");
        logger.LogInformation("Database migrations are automatically applied by the application on startup.");
        logger.LogInformation("Please start the TaskManager.Api or TaskManager.Web2 application to apply migrations.");
        logger.LogInformation("This console tool is only used for design-time operations (adding/removing migrations).");

        // Exit with success - no migrations applied
        return;
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                var connectionString = context.Configuration.GetConnectionString("DefaultConnection");
                
                services.AddDbContext<TaskManagerDbContext>(options =>
                    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
                
                services.AddLogging();
            });

}