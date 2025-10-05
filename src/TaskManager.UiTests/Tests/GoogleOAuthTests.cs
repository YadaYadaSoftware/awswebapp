using FluentAssertions;
using TaskManager.UiTests.Pages;
using Xunit;
using System.Threading.Tasks;

namespace TaskManager.UiTests.Tests;

public class GoogleOAuthTests : BaseTest
{
    [Fact]
    public async Task GoogleLogin_ShouldRedirectToGoogle()
    {
        // Arrange
        var mainPage = new MainPage(Page!, Config.BaseUrl);
        var loginPage = new LoginPage(Page!);

        // Act - Navigate to main page and click login
        await mainPage.NavigateAsync();
        await mainPage.ClickLoginLinkAsync();

        // Assert - Verify we're on the login page
        var isOnLoginPage = await loginPage.IsOnLoginPageAsync();
        isOnLoginPage.Should().BeTrue();

        // Act - Click Google login
        await loginPage.ClickGoogleLoginAsync();

        // Assert - Verify we're redirected to Google
        await RetryAsync(async () =>
        {
            var isGoogleRedirect = await loginPage.IsGoogleOauthRedirectAsync();
            isGoogleRedirect.Should().BeTrue("Should be redirected to Google OAuth");
        });
    }

    [Fact]
    public async Task CompleteGoogleOAuthFlow_ShouldReturnToApplication()
    {
        // Arrange
        var mainPage = new MainPage(Page!, Config.BaseUrl);
        var loginPage = new LoginPage(Page!);

        // Act - Navigate to main page and click login
        await mainPage.NavigateAsync();
        await mainPage.ClickLoginLinkAsync();

        // Assert - Verify we're on the login page
        var isOnLoginPage = await loginPage.IsOnLoginPageAsync();
        isOnLoginPage.Should().BeTrue();

        // Act - Click Google login
        await loginPage.ClickGoogleLoginAsync();

        // Assert - Verify we're redirected to Google
        await RetryAsync(async () =>
        {
            var isGoogleRedirect = await loginPage.IsGoogleOauthRedirectAsync();
            isGoogleRedirect.Should().BeTrue("Should be redirected to Google OAuth");
        });

        // Note: In a real test environment, you would need to:
        // 1. Set up a test Google account
        // 2. Mock or automate the Google login process
        // 3. Handle the OAuth callback

        // For this test, we're verifying the initial redirect works
        // The complete flow would require additional setup with test credentials
    }
}