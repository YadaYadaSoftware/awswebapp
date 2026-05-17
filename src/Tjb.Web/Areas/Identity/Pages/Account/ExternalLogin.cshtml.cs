using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Tjb.Web.Services;

namespace Tjb.Web.Areas.Identity.Pages.Account
{
    public class ExternalLoginModel : PageModel
    {
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
                ErrorMessage = $"Error from external provider: {remoteError}";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ErrorMessage = "Error loading external login information.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            // Try to sign in if user already has a linked external login
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

            // New user — auto-register with the email from the external provider
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(email))
            {
                ErrorMessage = $"The external provider '{info.LoginProvider}' did not return an email address. Cannot create account.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            var user = new IdentityUser { UserName = email, Email = email };
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                ErrorMessage = string.Join("; ", createResult.Errors.Select(e => e.Description));
                _logger.LogWarning("Failed to create user for {Email}: {Errors}", email, ErrorMessage);
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            var addLoginResult = await _userManager.AddLoginAsync(user, info);
            if (!addLoginResult.Succeeded)
            {
                ErrorMessage = string.Join("; ", addLoginResult.Errors.Select(e => e.Description));
                _logger.LogWarning("Failed to link external login for {Email}: {Errors}", email, ErrorMessage);
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            _logger.LogInformation("Auto-created user {Email} from {LoginProvider} provider.", email, info.LoginProvider);

            await SendConfirmationEmailAsync(user, email);

            return RedirectToPage("./RegisterConfirmation", new { email, returnUrl });
        }

        private async Task SendConfirmationEmailAsync(IdentityUser user, string email)
        {
            try
            {
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var encodedToken = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));

                var confirmUrl = Url.Page(
                    "/Account/ConfirmEmail",
                    pageHandler: null,
                    values: new { area = "Identity", email, token = encodedToken },
                    protocol: Request.Scheme) ?? string.Empty;

                var subject = "Confirm your email";
                var textBody =
                    $"Welcome!\n\nPlease confirm your email address by visiting this link:\n{confirmUrl}\n\nIf you did not create this account, you can ignore this email.";

                var htmlBody = await _viewRenderService.RenderToStringAsync(
                    "/Pages/EmailTemplates/ConfirmationEmail.cshtml",
                    new ConfirmationEmailViewModel { Email = email, ConfirmationUrl = confirmUrl });

                await _emailService.SendEmailAsync(new SendEmailRequest
                {
                    To = email,
                    Subject = subject,
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
