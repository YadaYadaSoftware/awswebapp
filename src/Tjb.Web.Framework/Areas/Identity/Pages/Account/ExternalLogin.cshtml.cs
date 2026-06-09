using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Tjb.Web.Services;

namespace Tjb.Web.Areas.Identity.Pages.Account
{
    public class ExternalLoginModel : PageModel
    {
        private const string ConfirmationEmailView = "/Pages/EmailTemplates/ConfirmationEmail.cshtml";

        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailService _emailService;
        private readonly IViewRenderService _viewRenderService;
        private readonly ILogger<ExternalLoginModel> _logger;

        public ExternalLoginModel(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            IEmailService emailService,
            IViewRenderService viewRenderService,
            ILogger<ExternalLoginModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _emailService = emailService;
            _viewRenderService = viewRenderService;
            _logger = logger;
        }

        public string? ErrorMessage { get; set; }

        public IActionResult OnGet() => RedirectToPage("./Login");

        public IActionResult OnPost(string provider, string? returnUrl = null)
        {
            var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return new ChallengeResult(provider, properties);
        }

        public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
        {
            returnUrl ??= Url.Content("~/");

            if (remoteError != null)
            {
                return LoginError($"Error from external provider: {remoteError}", returnUrl);
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                return LoginError("Error loading external login information.", returnUrl);
            }

            var signInResult = await _signInManager.ExternalLoginSignInAsync(
                info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

            if (signInResult.Succeeded)
            {
                _logger.LogInformation("{Name} signed in with {LoginProvider} provider.",
                    info.Principal.Identity?.Name, info.LoginProvider);
                return LocalRedirect(returnUrl);
            }

            if (signInResult.IsLockedOut)
            {
                return RedirectToPage("./Lockout");
            }

            // Either no linked external login OR sign-in was blocked (e.g. IsNotAllowed because
            // EmailConfirmed = false). Either way, fall through to existing-user-by-email handling.

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(email))
            {
                return LoginError($"The external provider '{info.LoginProvider}' did not return an email address. Cannot create account.", returnUrl);
            }

            // If a user with this email already exists, link the external login to that account
            // (Google verifies email ownership, so this is safe).
            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                var existingLogins = await _userManager.GetLoginsAsync(existingUser);
                var alreadyLinked = existingLogins.Any(l =>
                    l.LoginProvider == info.LoginProvider && l.ProviderKey == info.ProviderKey);

                if (!alreadyLinked)
                {
                    var linkResult = await _userManager.AddLoginAsync(existingUser, info);
                    if (!linkResult.Succeeded)
                    {
                        var errors = FormatIdentityErrors(linkResult);
                        _logger.LogWarning("Failed to link {Provider} login to existing user {Email}: {Errors}",
                            info.LoginProvider, email, errors);
                        return LoginError(errors, returnUrl);
                    }
                    _logger.LogInformation("Linked {Provider} login to existing user {Email}.", info.LoginProvider, email);
                }

                if (!existingUser.EmailConfirmed)
                {
                    _logger.LogInformation("Existing user {Email} is not confirmed; resending confirmation email.", email);
                    await SendConfirmationEmailAsync(existingUser, email);
                    return RedirectToPage("./RegisterConfirmation", new { email });
                }

                await _signInManager.SignInAsync(existingUser, isPersistent: false);
                _logger.LogInformation("Signed in existing user {Email} after linking {Provider}.", email, info.LoginProvider);
                return LocalRedirect(returnUrl);
            }

            // No existing user — auto-create with the email from the external provider.
            var user = new IdentityUser { UserName = email, Email = email };
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                var errors = FormatIdentityErrors(createResult);
                _logger.LogWarning("Failed to create user for {Email}: {Errors}", email, errors);
                return LoginError(errors, returnUrl);
            }

            var addLoginResult = await _userManager.AddLoginAsync(user, info);
            if (!addLoginResult.Succeeded)
            {
                var errors = FormatIdentityErrors(addLoginResult);
                _logger.LogWarning("Failed to link external login for {Email}: {Errors}", email, errors);
                return LoginError(errors, returnUrl);
            }

            _logger.LogInformation("Auto-created user {Email} from {LoginProvider} provider.", email, info.LoginProvider);

            await SendConfirmationEmailAsync(user, email);

            return RedirectToPage("./RegisterConfirmation", new { email });
        }

        private IActionResult LoginError(string message, string returnUrl)
        {
            ErrorMessage = message;
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        private static string FormatIdentityErrors(IdentityResult result) =>
            string.Join("; ", result.Errors.Select(e => e.Description));

        private async Task SendConfirmationEmailAsync(IdentityUser user, string email)
        {
            try
            {
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

                var confirmUrl = Url.Page(
                    "/Account/ConfirmEmail",
                    pageHandler: null,
                    values: new { area = "Identity", email, token = encodedToken },
                    protocol: Request.Scheme) ?? string.Empty;

                var textBody =
                    $"Welcome!\n\nPlease confirm your email address by visiting this link:\n{confirmUrl}\n\nIf you did not create this account, you can ignore this email.";

                var htmlBody = await _viewRenderService.RenderToStringAsync(
                    ConfirmationEmailView,
                    new ConfirmationEmailViewModel { Email = email, ConfirmationUrl = confirmUrl });

                await _emailService.SendEmailAsync(new SendEmailRequest
                {
                    To = email,
                    Subject = "Confirm your email",
                    HtmlBody = htmlBody,
                    TextBody = textBody,
                });

                _logger.LogInformation("Confirmation email queued for {Email}.", email);
            }
            catch (Exception ex)
            {
                // Don't block registration on email failures — user can request a resend later.
                _logger.LogError(ex, "Failed to send confirmation email for {Email}.", email);
            }
        }
    }
}
