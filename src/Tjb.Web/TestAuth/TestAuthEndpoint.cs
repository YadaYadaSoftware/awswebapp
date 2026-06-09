using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;

namespace Tjb.Web.TestAuth;

/// <summary>
/// Test-only, environment-gated sign-in endpoint. Validates a genuine Google <c>id_token</c>
/// (signature against Google's keys, issuer, audience = the configured Google client id, expiry)
/// and, on success, issues a REAL ASP.NET Identity session cookie via <see cref="SignInManager{TUser}"/>.
///
/// This lets the UI test suite reach authenticated pages with a real session instead of injecting a
/// bogus cookie (which the app correctly ignores — see the ui-test-authenticated-session change).
///
/// SAFETY: the endpoint is mapped ONLY when <c>TestAuth:Enabled</c> is true. The deploy templates set
/// that flag false on the shared-infrastructure environments (app/beta/alpha), so the endpoint is
/// absent (404) there. It never bypasses Google verification — it requires a real id_token minted for
/// our client.
/// </summary>
public static class TestAuthEndpoint
{
    private const string LogCategory = "Tjb.Web.TestAuth";

    public static WebApplication MapAwsWebAppTestAuth(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);

        if (!app.Configuration.GetValue("TestAuth:Enabled", false))
        {
            logger.LogInformation("TestAuth endpoint is DISABLED (TestAuth:Enabled=false); /test-auth/signin not mapped.");
            return app;
        }

        logger.LogWarning("TestAuth endpoint is ENABLED at POST /test-auth/signin. This must never be enabled in production (app/beta/alpha).");

        app.MapPost("/test-auth/signin", async (
            HttpContext http,
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            IConfiguration config,
            ILoggerFactory loggerFactory) =>
        {
            var log = loggerFactory.CreateLogger(LogCategory);

            // Accept the id_token from a form field or an Authorization: Bearer header.
            string? idToken = http.Request.HasFormContentType ? http.Request.Form["id_token"].ToString() : null;
            if (string.IsNullOrEmpty(idToken))
            {
                var authHeader = http.Request.Headers.Authorization.ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    idToken = authHeader["Bearer ".Length..].Trim();
                }
            }
            if (string.IsNullOrEmpty(idToken))
            {
                return Results.BadRequest("missing id_token (provide as form field 'id_token' or 'Authorization: Bearer <id_token>')");
            }

            var clientId = config["Authentication:Google:ClientId"];
            if (string.IsNullOrEmpty(clientId))
            {
                log.LogError("TestAuth: no Authentication:Google:ClientId configured; cannot validate id_token audience.");
                return Results.Problem("server has no Google client id configured", statusCode: StatusCodes.Status500InternalServerError);
            }

            GoogleJsonWebSignature.Payload payload;
            try
            {
                // Validates signature (Google JWKS), issuer (accounts.google.com), expiry, and audience.
                payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId }
                });
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "TestAuth: id_token validation failed.");
                return Results.Unauthorized();
            }

            var email = payload.Email;
            if (string.IsNullOrEmpty(email))
            {
                log.LogWarning("TestAuth: validated id_token carried no email claim.");
                return Results.Unauthorized();
            }

            // Resolve or provision the Identity user (mirrors the external-login provisioning).
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                var created = await userManager.CreateAsync(user);
                if (!created.Succeeded)
                {
                    var errors = string.Join("; ", created.Errors.Select(e => e.Description));
                    log.LogError("TestAuth: failed to provision user {Email}: {Errors}", email, errors);
                    return Results.Problem($"could not provision test user: {errors}", statusCode: StatusCodes.Status500InternalServerError);
                }
                log.LogInformation("TestAuth: provisioned user {Email}.", email);
            }
            else if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await userManager.UpdateAsync(user);
            }

            // Issue the genuine .AspNetCore.Identity.Application cookie.
            await signInManager.SignInAsync(user, isPersistent: false);
            log.LogInformation("TestAuth: signed in {Email}.", email);
            return Results.Ok(new { email });
        });

        return app;
    }
}
