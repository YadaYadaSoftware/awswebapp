using Microsoft.Playwright;
using System.Threading.Tasks;
using static Microsoft.Playwright.Assertions;

namespace Tjb.UiTests.Pages;

public class MainPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public MainPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    private ILocator LoginLink => _page.Locator("a[href*='Identity/Account/Login']");
    private ILocator UserGreeting => _page.Locator("a[title='Manage']:has-text('Hello')");

    // Expect-based assertions: built-in waiting/retry so SSR + ALB latency doesn't
    // surface as an element-not-ready race. Use these in assertion context instead
    // of the instantaneous Is*VisibleAsync bool checks.
    public async Task ExpectLoginLinkVisibleAsync(float timeout = 10_000)
        => await Expect(LoginLink).ToBeVisibleAsync(new() { Timeout = timeout });

    public async Task ExpectUserLoggedInAsync(float timeout = 10_000)
        => await Expect(UserGreeting).ToBeVisibleAsync(new() { Timeout = timeout });

    public async Task NavigateAsync()
    {
        await _page.GotoAsync(_baseUrl);
    }

    public async Task ClickLoginLinkAsync()
    {
        // Try multiple possible login link selectors for better reliability
        try
        {
            // First try the specific href pattern
            await _page.ClickAsync("a[href*='Identity/Account/Login']");
        }
        catch
        {
            // Fallback to text-based selector if href-based fails
            await _page.ClickAsync("a:has-text('Log in')");
        }
    }

    public async Task<bool> IsLoginLinkVisibleAsync()
    {
        return await _page.IsVisibleAsync("a[href*='Identity/Account/Login']");
    }

    public async Task<bool> IsUserLoggedInAsync()
    {
        return await _page.IsVisibleAsync("a[title='Manage']:has-text('Hello')");
    }

    public async Task<string> GetLoggedInUserNameAsync()
    {
        var userElement = _page.Locator("a[title='Manage']:has-text('Hello')");
        var text = await userElement.TextContentAsync();
        return text?.Replace("Hello ", "").Replace("!", "").Trim() ?? string.Empty;
    }
}