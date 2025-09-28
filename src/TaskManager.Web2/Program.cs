using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql;
using TaskManager.Web2.Areas.Identity;
using TaskManager.Web2.Data;
using TaskManager.Web2.Services;
using static Microsoft.Extensions.DependencyInjection.GoogleExtensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    // Use MySQL for both development and production
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Events.OnRedirectToLogin = context =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Redirecting to login from {Path}", context.Request.Path);
        return Task.CompletedTask;
    };

    options.Events.OnSignedIn = context =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("User signed in: {UserName}", context.Principal?.Identity?.Name ?? "Unknown");
        return Task.CompletedTask;
    };

    options.Events.OnSigningIn = context =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("User signing in: {UserName}", context.Principal?.Identity?.Name ?? "Unknown");
        return Task.CompletedTask;
    };

    options.Events.OnSigningOut = context =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("User signing out: {UserName}", context.HttpContext.User?.Identity?.Name ?? "Unknown");
        return Task.CompletedTask;
    };
});
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider<IdentityUser>>();
builder.Services.AddSingleton<WeatherForecastService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddHealthChecks();
builder.Services.AddAuthentication().AddGoogle(googleOptions =>
{
    googleOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"];
    googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

    googleOptions.Events.OnCreatingTicket = async context =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Google OAuth creating ticket for user: {User}", context.Identity?.Name ?? "Unknown");

        try
        {
            // Test database connectivity
            var dbContext = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
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
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(context.Failure, "Google OAuth remote failure");
        return Task.CompletedTask;
    };

    googleOptions.Events.OnTicketReceived = context =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Google OAuth ticket received");
        return Task.CompletedTask;
    };
});

var app = builder.Build();

// Log Google OAuth configuration status
using var scope = app.Services.CreateScope();
var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
var scopedConfig = scope.ServiceProvider.GetRequiredService<IConfiguration>();
var clientId = scopedConfig["Authentication:Google:ClientId"];
var clientSecret = scopedConfig["Authentication:Google:ClientSecret"];
scopedLogger.LogInformation("Google OAuth ClientId configured: {Configured}", !string.IsNullOrEmpty(clientId));
scopedLogger.LogInformation("Google OAuth ClientSecret configured: {Configured}", !string.IsNullOrEmpty(clientSecret));

// Apply database migrations on startup
await ApplyDatabaseMigrations(app);

// Configure forwarded headers for ALB
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// Clear known proxies/networks to allow ALB (which has dynamic IPs)
forwardedHeadersOptions.KnownProxies.Clear();
forwardedHeadersOptions.KnownNetworks.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Add authentication middleware here to ensure forwarded headers are applied first
app.UseAuthentication();

// Log forwarded headers for debugging
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/signin-google")
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Forwarded headers - Proto: {Proto}, Host: {Host}, Path: {Path}",
            context.Request.Headers["X-Forwarded-Proto"], context.Request.Host, context.Request.Path);
        logger.LogInformation("Request scheme: {Scheme}, IsHttps: {IsHttps}", context.Request.Scheme, context.Request.IsHttps);
    }
    await next();
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

async Task ApplyDatabaseMigrations(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var context = services.GetRequiredService<ApplicationDbContext>();

    try
    {
        logger.LogInformation("Ensuring database exists and applying migrations for ApplicationDbContext...");

        // This will create the database if it doesn't exist
        await context.Database.EnsureCreatedAsync();

        // This will apply all pending migrations
        await context.Database.MigrateAsync();

        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations.");
        // Don't throw - let the application start even if migrations fail
        // This prevents application startup issues in production
    }
}
