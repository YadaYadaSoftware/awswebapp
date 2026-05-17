using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Tjb.Web.Areas.Identity.Pages.Account
{
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<ConfirmEmailModel> _logger;

        public ConfirmEmailModel(UserManager<IdentityUser> userManager, ILogger<ConfirmEmailModel> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        public enum ResultState
        {
            Invalid,
            Confirmed,
            AlreadyConfirmed,
            Expired,
        }

        public string Email { get; set; } = string.Empty;
        public ResultState State { get; set; } = ResultState.Invalid;
        public string ErrorMessage { get; set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync(string? email, string? token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                State = ResultState.Invalid;
                ErrorMessage = "Missing email or token in the URL.";
                return Page();
            }

            Email = email;

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                State = ResultState.Invalid;
                ErrorMessage = "No matching account.";
                _logger.LogWarning("ConfirmEmail: no user found for {Email}.", email);
                return Page();
            }

            if (user.EmailConfirmed)
            {
                State = ResultState.AlreadyConfirmed;
                return Page();
            }

            string decodedToken;
            try
            {
                decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            }
            catch (FormatException)
            {
                State = ResultState.Invalid;
                ErrorMessage = "The confirmation token is malformed.";
                _logger.LogWarning("ConfirmEmail: malformed token for {Email}.", email);
                return Page();
            }

            var result = await _userManager.ConfirmEmailAsync(user, decodedToken);
            if (result.Succeeded)
            {
                State = ResultState.Confirmed;
                _logger.LogInformation("ConfirmEmail: confirmed {Email}.", email);
                return Page();
            }

            // Identity returns the same "InvalidToken" code for both expired and malformed tokens.
            // We treat it as expired here because malformed tokens were caught above by the decode try/catch.
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
            {
                State = ResultState.Expired;
                _logger.LogInformation("ConfirmEmail: expired/invalid token for {Email}.", email);
                return Page();
            }

            State = ResultState.Invalid;
            ErrorMessage = string.Join("; ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("ConfirmEmail: failed for {Email}: {Errors}", email, ErrorMessage);
            return Page();
        }
    }
}
