using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using TaskManager.Data;
using TaskManager.Web.Data;
using AWS.Logger.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Get version information
var informationalVersion = Attribute.GetCustomAttribute(typeof(Program).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute)) as System.Reflection.AssemblyInformationalVersionAttribute;
var version = informationalVersion?.InformationalVersion ?? "1.0.0.0";

// Configure logging to CloudWatch
builder.Logging.AddAWSProvider();

// Add version to logging scope
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Services.AddLogging(logging =>
{
    logging.AddFilter("Microsoft", LogLevel.Warning);
    logging.AddFilter("System", LogLevel.Warning);
});

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();
builder.Services.AddAntiforgery();

// Add Entity Framework
builder.Services.AddDbContext<TaskManagerDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString);
});

// Add authentication with Google OAuth
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "Cookies";
    options.DefaultSignInScheme = "Cookies";
    options.DefaultChallengeScheme = "Google";
})
.AddCookie("Cookies", options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
    options.SlidingExpiration = true;
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/login";
})
.AddGoogle("Google", options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? throw new InvalidOperationException("Google ClientId not configured");
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? throw new InvalidOperationException("Google ClientSecret not configured");
    options.SaveTokens = true;

    // Add scopes for user information
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");

    // Configure correlation cookie
    options.CorrelationCookie.SameSite = SameSiteMode.Lax;
    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

    // Force HTTPS for OAuth callbacks
    options.CallbackPath = "/signin-google";

    // Ensure HTTPS is used for OAuth redirects
    options.Events.OnRedirectToAuthorizationEndpoint = context =>
    {
        context.Response.Redirect(context.RedirectUri.Replace("http://", "https://"));
        return System.Threading.Tasks.Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    // Require authentication for all pages by default
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint - EXCLUDED FROM AUTHENTICATION
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .AllowAnonymous()
   .WithName("HealthCheck")
   .WithTags("Health");

// Login and logout endpoints - EXCLUDED FROM AUTHENTICATION
app.MapGet("/login", () => Results.Redirect("/"))
    .AllowAnonymous()
    .WithName("Login");

app.MapGet("/logout", async (HttpContext context) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    using (logger.BeginScope(new Dictionary<string, object> { ["Version"] = version }))
    {
        logger.LogInformation("Logout endpoint called - Version: {Version}", version);

        logger.LogInformation("Signing out from Cookies authentication scheme");
        await context.SignOutAsync("Cookies");

        logger.LogInformation("Logout completed, redirecting to Google sign-out");
    }

    // Redirect to Google sign-out to clear Google session
    var googleLogoutUrl = "https://accounts.google.com/logout?continue=" + Uri.EscapeDataString(context.Request.Scheme + "://" + context.Request.Host + "/");
    return Results.Redirect(googleLogoutUrl);
})
.AllowAnonymous()
.WithName("Logout");

// Google OAuth callback endpoints - EXCLUDED FROM AUTHENTICATION
app.MapGet("/signin-google", () => Results.Redirect("/"))
   .AllowAnonymous()
   .WithName("GoogleSignIn");

app.MapGet("/signout-google", () => Results.Redirect("/"))
   .AllowAnonymous()
   .WithName("GoogleSignOut");

// Error page - EXCLUDED FROM AUTHENTICATION
app.MapGet("/Error", () => Results.Content("<h1>Application Error</h1><p>An error occurred while processing your request.</p>", "text/html"))
   .AllowAnonymous()
   .WithName("Error");

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
