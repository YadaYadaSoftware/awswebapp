using Microsoft.Playwright;
using System.Threading.Tasks;

namespace TaskManager.UiTests.Pages;

public class MainPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public MainPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    public async Task NavigateAsync()
    {
        await _page.GotoAsync(_baseUrl);
    }

    public async Task ClickLoginLinkAsync()
    {
        await _page.ClickAsync("a[href*='/Identity/Account/Login']");
    }

    public async Task<bool> IsLoginLinkVisibleAsync()
    {
        var x = _page.Locator("a:has-text('Log in')");
        return await _page.IsVisibleAsync("a:has-text('Log in')");
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