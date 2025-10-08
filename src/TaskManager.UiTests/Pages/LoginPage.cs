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


    public async Task<bool> IsOAuthCallbackReceivedAsync()
    {
        return _page.Url.Contains("code=") && _page.Url.Contains("state=");
    }

    public async Task<bool> IsBackInApplicationAsync()
    {
        // Check if we're back in the application (not on Google domains)
        return !_page.Url.Contains("accounts.google.com") && !_page.Url.Contains("google.com");
    }


    private async Task CaptureScreenshotAsync(string screenshotName)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"{screenshotName}_{timestamp}.png";
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = filename, FullPage = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to capture screenshot: {ex.Message}");
        }
    }

    public async Task ClearCookiesAndStorageAsync()
    {
        try
        {
            // Clear all cookies and storage to ensure clean state
            await _page.Context.ClearCookiesAsync();
            await _page.EvaluateAsync("localStorage.clear()");
            await _page.EvaluateAsync("sessionStorage.clear()");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to clear cookies and storage: {ex.Message}");
        }
    }

    public async Task AuthenticateWithTokenAsync(string accessToken)
    {
        // Instead of going through the OAuth flow, we'll simulate being authenticated
        // by setting the token in localStorage or using it directly with the API

        // For testing purposes, we'll assume the application accepts tokens via a specific endpoint
        // or we can set authentication cookies directly

        // Option 1: Set authentication cookie directly (if your app supports it)
        await _page.Context.AddCookiesAsync(new[]
        {
            new Cookie
            {
                Name = ".AspNetCore.Identity.Application",
                Value = accessToken,
                Domain = new Uri(_page.Url).Host,
                Path = "/"
            }
        });

        // Option 2: If your app has a token endpoint, use it
        // await _page.EvaluateAsync($"fetch('/api/auth/token', {{ method: 'POST', headers: {{ 'Content-Type': 'application/json' }}, body: JSON.stringify({{ token: '{accessToken}' }}) }})");

        // Wait a moment for authentication to take effect
        await _page.WaitForTimeoutAsync(2000);
    }
}