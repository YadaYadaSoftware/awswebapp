using Microsoft.EntityFrameworkCore;
using Tjb.Data;
using Tjb.Web.Data;
using Tjb.Web.TestAuth;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Register TjbDbContext for both Identity and application data
builder.Services.AddDbContext<TjbDbContext>(options =>
{
    // Use MySQL for both development and production
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), mysqlOptions =>
    {
        // This ensures database exists before connecting
        mysqlOptions.EnableRetryOnFailure(3);
    });
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Tjb web framework wiring (see Tjb.Web.Hosting).
builder.Services.AddAwsWebAppIdentity<TjbDbContext>();
builder.Services.AddAwsWebAppGoogleAuth<TjbDbContext>(builder.Configuration);
builder.Services.AddAwsWebAppEmail(builder.Configuration);

// Host-owned registrations.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Log Google OAuth configuration status
app.LogAwsWebAppAuthConfig();

// Apply database migrations on startup in all environments
await app.ApplyDatabaseMigrationsAsync<TjbDbContext>();

// Configure forwarded headers for ALB (dynamic IPs)
app.UseAwsWebAppForwardedHeaders();

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

// Test-only, environment-gated sign-in endpoint (no-op unless TestAuth:Enabled=true).
// Lets the UI suite establish a real Identity session; off on app. See TestAuthEndpoint.
app.MapAwsWebAppTestAuth();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
