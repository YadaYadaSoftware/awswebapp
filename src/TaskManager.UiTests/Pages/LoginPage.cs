using Microsoft.Playwright;
using System.Threading.Tasks;
using System;

namespace TaskManager.UiTests.Pages;

public class LoginPage
{
    private readonly IPage _page;

    public LoginPage(IPage page)
    {
        _page = page;
    }

    public async Task<bool> IsOnLoginPageAsync()
    {
        return _page.Url.Contains("Identity/Account/Login");
    }

    public async Task ClickGoogleLoginAsync()
    {
        var button = _page.Locator("button:has-text('Google'), a:has-text('Google')");
        if (button == null)
        {
            throw new Exception("Google login button not found on the login page.");
        }
        await button.ClickAsync();
        // await _page.ClickAsync("button, a, div:has-text('Google'), [data-provider='Google']");
    }

    public async Task<bool> IsGoogleLoginVisibleAsync()
    {
        // Look for Google login button/link - this may vary based on ASP.NET Identity implementation
        var selectors = new[]
        {
            "button:has-text('Google')",
            "a:has-text('Google')",
            "div:has-text('Google')",
            "[data-provider='Google']",
            ".provider-google"
        };

        foreach (var selector in selectors)
        {
            if (await _page.IsVisibleAsync(selector))
                return true;
        }

        return false;
    }

    public async Task<bool> IsGoogleOauthRedirectAsync()
    {
        return _page.Url.Contains("accounts.google.com");
    }
}