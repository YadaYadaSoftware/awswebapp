using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Tjb.Web.Areas.Identity.Pages.Account
{
    public class RegisterConfirmationModel : PageModel
    {
        public string Email { get; set; } = string.Empty;

        public IActionResult OnGet(string? email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return RedirectToPage("./Login");
            }

            Email = email;
            return Page();
        }
    }
}
