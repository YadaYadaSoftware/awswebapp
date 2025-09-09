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
