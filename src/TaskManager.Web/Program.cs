using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
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
builder.Services.AddHttpContextAccessor();

// Add Entity Framework
builder.Services.AddDbContext<TaskManagerDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString);
});

// Add authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie()
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
    options.SaveTokens = true;

});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// Custom logout to sign out from cookie scheme
// app.MapPost("/logout", async (HttpContext context) =>
// {
//     await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
//     context.Response.Redirect("/logout");
// });
app.MapPost("/logout", async context =>
{
    // OPTIONAL: Revoke the tokens your app has (access or refresh token)
    var accessToken = await context.GetTokenAsync("access_token");
    var refreshToken = await context.GetTokenAsync("refresh_token");

    async Task RevokeAsync(string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        using var http = new HttpClient();
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("token", token)
        });
        // Google token revocation endpoint
        await http.PostAsync("https://oauth2.googleapis.com/revoke", content);
    }

    await RevokeAsync(refreshToken ?? accessToken);

    // Sign out of YOUR app (clears cookie)
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    // Send them somewhere nice
    context.Response.Redirect("/");
});

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

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithTags("Health");

// Error page
app.MapGet("/Error", () => Results.Content("<h1>Application Error</h1><p>An error occurred while processing your request.</p>", "text/html"))
    .WithName("Error");
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
