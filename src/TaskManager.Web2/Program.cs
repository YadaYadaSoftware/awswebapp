using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.EntityFrameworkCore;
using TaskManager.Web2.Areas.Identity;
using TaskManager.Web2.Data;
using static Microsoft.Extensions.DependencyInjection.GoogleExtensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    // Use SQL Server in development, PostgreSQL in production
    if (builder.Environment.IsDevelopment())
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        options.UseNpgsql(connectionString);
    }
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider<IdentityUser>>();
builder.Services.AddSingleton<WeatherForecastService>();
builder.Services.AddHealthChecks();
var configuration = builder.Configuration;
builder.Services.AddAntiforgery(options =>
{
    options.ExcludedPaths.Add("/signin-google");
});

builder.Services.AddAuthentication().AddGoogle(googleOptions =>
{
    googleOptions.ClientId = configuration["Authentication:Google:ClientId"];
    googleOptions.ClientSecret = configuration["Authentication:Google:ClientSecret"];
    googleOptions.Events = new Microsoft.AspNetCore.Authentication.OAuth.OAuthEvents
    {
        OnRedirectToAuthorizationEndpoint = context =>
        {
            var uri = new UriBuilder(context.RedirectUri);
            uri.Scheme = context.HttpContext.Request.Scheme;
            uri.Host = context.HttpContext.Request.Host;
            context.RedirectUri = uri.ToString();
            context.Response.Redirect(context.RedirectUri);
            context.HandleResponse();
        }
    };
});

var app = builder.Build();

// Configure forwarded headers for ALB
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// Clear known proxies/networks to allow ALB (which has dynamic IPs)
forwardedHeadersOptions.KnownProxies.Clear();
forwardedHeadersOptions.KnownNetworks.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

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
