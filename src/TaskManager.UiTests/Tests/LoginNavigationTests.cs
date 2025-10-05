using FluentAssertions;
using TaskManager.UiTests.Pages;
using Xunit;
using System.Threading.Tasks;
using System;

namespace TaskManager.UiTests.Tests;

public class LoginNavigationTests : BaseTest
{
    [Fact]
    public async Task LoginLink_ShouldNavigateToLoginPage()
    {
        // Arrange
        var mainPage = new MainPage(Page!, Config.BaseUrl);
        var loginPage = new LoginPage(Page!);

        // Act
        await mainPage.NavigateAsync();

        // Assert - Verify we're on the main page and login link is visible
        var isLoginVisible = await mainPage.IsLoginLinkVisibleAsync();
        isLoginVisible.Should().BeTrue("Login link should be visible on main page");

        // Act - Click the login link
        await mainPage.ClickLoginLinkAsync();

        // Assert - Verify we're redirected to the login page
        var isOnLoginPage = await loginPage.IsOnLoginPageAsync();
        isOnLoginPage.Should().BeTrue("Should be redirected to login page after clicking login link");
    }

    [Fact]
    public async Task LoginPage_ShouldDisplayGoogleLoginOption()
    {
        // Arrange
        var mainPage = new MainPage(Page!, Config.BaseUrl);
        var loginPage = new LoginPage(Page!);

        // Act
        await mainPage.NavigateAsync();
        await mainPage.ClickLoginLinkAsync();

        // Assert - Verify we're on the login page
        var isOnLoginPage = await loginPage.IsOnLoginPageAsync();
        isOnLoginPage.Should().BeTrue();

        // Assert - Verify Google login option is available
        var isGoogleVisible = await loginPage.IsGoogleLoginVisibleAsync();
        isGoogleVisible.Should().BeTrue("Google login option should be visible on login page");

        // Capture a screenshot for verification
        await CaptureScreenshotAsync("login_page_success");
    }

    [Fact]
    public async Task ForceFailure_ShouldCaptureScreenshot()
    {
        // Set the current test name for screenshot capture
        SetCurrentTestName("ForceFailure_ShouldCaptureScreenshot");

        // Arrange
        var mainPage = new MainPage(Page!, Config.BaseUrl);

        // Act & Assert - Use RetryAsync to trigger screenshot capture on failure
        await RetryAsync(async () =>
        {
            await mainPage.NavigateAsync();

            // Force a failure to test screenshot capture
            Console.WriteLine("About to check for non-existent element...");
            var elementExists = await Page!.IsVisibleAsync("non-existent-element");
            Console.WriteLine($"Element exists: {elementExists}");

            // This will fail and trigger screenshot capture through RetryAsync
            elementExists.Should().BeTrue("This test is designed to fail to test screenshot functionality");
        }, maxAttempts: 1); // Only one attempt to force failure

        // Also capture a screenshot manually for testing
        Console.WriteLine("Capturing manual screenshot...");
        await CaptureScreenshotAsync("manual_test_screenshot");
    }
}