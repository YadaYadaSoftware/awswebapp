using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tjb.Web.Areas.Identity;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Identity wiring for the Tjb web framework: default Identity with EF stores,
/// the cookie-event logging, and the revalidating auth-state provider.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    private const string LogCategory = "Tjb.Web.Hosting.Identity";

    /// <summary>
    /// Registers ASP.NET Identity (<see cref="IdentityUser"/>) backed by <typeparamref name="TContext"/>,
    /// with <c>RequireConfirmedAccount = true</c>, the application-cookie event logging, and the
    /// <see cref="RevalidatingIdentityAuthenticationStateProvider{TUser}"/> auth-state provider.
    /// </summary>
    public static IServiceCollection AddAwsWebAppIdentity<TContext>(
        this IServiceCollection services,
        Action<IdentityOptions>? configureIdentity = null)
        where TContext : DbContext
    {
        services.AddDefaultIdentity<IdentityUser>(options =>
        {
            options.SignIn.RequireConfirmedAccount = true;
            configureIdentity?.Invoke(options);
        })
        .AddEntityFrameworkStores<TContext>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnRedirectToLogin = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
                logger.LogInformation("Redirecting to login from {Path}", context.Request.Path);
                return Task.CompletedTask;
            };

            options.Events.OnSignedIn = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
                logger.LogInformation("User signed in: {UserName}", context.Principal?.Identity?.Name ?? "Unknown");
                return Task.CompletedTask;
            };

            options.Events.OnSigningIn = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
                logger.LogInformation("User signing in: {UserName}", context.Principal?.Identity?.Name ?? "Unknown");
                return Task.CompletedTask;
            };

            options.Events.OnSigningOut = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
                logger.LogInformation("User signing out: {UserName}", context.HttpContext.User?.Identity?.Name ?? "Unknown");
                return Task.CompletedTask;
            };
        });

        services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider<IdentityUser>>();

        return services;
    }
}
