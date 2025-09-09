using Amazon.Lambda.Annotations;
using Amazon.Lambda.AspNetCoreServer;
using Amazon.Lambda.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Services;
using TaskManager.Data;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TaskManager.Api;

public class Program
{
    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure services
        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();

        // Apply database migrations on startup
        await ApplyDatabaseMigrations(app);

        // Configure pipeline
        ConfigurePipeline(app, app.Environment);

        app.Run();
    }

    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Add Entity Framework
        services.AddDbContext<TaskManagerDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            options.UseNpgsql(connectionString);
        });
        
        // Add migration service
        services.AddScoped<IDatabaseMigrationService, DatabaseMigrationService>();
        
        // Add invitation service
        services.AddScoped<IInvitationService, InvitationService>();

        // Add CORS
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        // Add API services
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new() { Title = "TaskManager API", Version = "v1" });
        });

        // Configure forwarded headers for proper HTTPS detection behind load balancer
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                                      ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // No authentication - API runs anonymously

        // Lambda Functions will be registered when we implement them properly
    }

    public static void ConfigurePipeline(WebApplication app, IWebHostEnvironment env)
    {
        // Configure the HTTP request pipeline
        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "TaskManager API v1");
            });
        }

        app.UseCors();

        // Configure forwarded headers middleware
        app.UseForwardedHeaders();

        // Health check endpoint - must be before auth middleware
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
           .WithName("HealthCheck")
           .WithTags("Health")
           .AllowAnonymous(); // Explicitly allow anonymous access


        // API endpoints will be added via Lambda Annotations in separate controller classes
    }

    private static async Task ApplyDatabaseMigrations(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();
        
        try
        {
            logger.LogInformation("Applying database migrations...");
            
            var migrationService = services.GetRequiredService<IDatabaseMigrationService>();
            await migrationService.MigrateAsync();
            
            logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying database migrations.");
            // Don't throw - let the application start even if migrations fail
            // This prevents Lambda cold start issues
        }
    }
}

/// <summary>
/// Lambda entry point for the ASP.NET Core application
/// </summary>
public class LambdaEntryPoint : Amazon.Lambda.AspNetCoreServer.APIGatewayProxyFunction
{
    protected override void Init(IWebHostBuilder builder)
    {
        builder.UseStartup<Startup>();
    }
}

/// <summary>
/// Startup class for Lambda hosting
/// </summary>
public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        Program.ConfigureServices(services, Configuration);
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // For Lambda, we need to handle this differently since we don't have WebApplication
        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "TaskManager API v1");
            });
        }

        app.UseCors();
        app.UseRouting();

        // Health check endpoint
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGet("/health", async context =>
            {
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { status = "healthy", timestamp = DateTime.UtcNow }));
            });
        });

    }
}
