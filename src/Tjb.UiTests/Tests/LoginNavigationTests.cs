using FluentAssertions;
using Tjb.UiTests.Pages;
using Xunit;
using System.Threading.Tasks;
using System;

namespace Tjb.UiTests.Tests;

public class LoginNavigationTests : BaseTest
{
    [Fact]
    public async Task LoginLink_ShouldNavigateToLoginPage()
    {
        // Set test name for screenshot capture
        SetCurrentTestName(nameof(LoginLink_ShouldNavigateToLoginPage));

        try
        {
            // Arrange
            var mainPage = new MainPage(Page!, Config.BaseUrl);
            var loginPage = new LoginPage(Page!);

            // Act
            await RetryAsync(async () =>
            {
                await mainPage.NavigateAsync();
            });


            // Assert - Verify we're on the main page and login link is visible
            var isLoginVisible = await mainPage.IsLoginLinkVisibleAsync();
            isLoginVisible.Should().BeTrue("Login link should be visible on main page");

            // Act - Click the login link
            await RetryAsync(async () =>
            {
                await mainPage.ClickLoginLinkAsync();
            });

            // Assert - Verify we're redirected to the login page
            var isOnLoginPage = await loginPage.IsOnLoginPageAsync();
            isOnLoginPage.Should().BeTrue("Should be redirected to login page after clicking login link");

            // Record test success
            RecordTestSuccess();
        }
        catch (Exception ex)
        {
            // Capture screenshot on failure
            await CaptureScreenshotAsync("failure");
            await CaptureFinalScreenshotAsync("failed");
            throw; // Re-throw to ensure test fails properly
        }
        finally
        {
            // Always capture final screenshot for debugging
            await CaptureFinalScreenshotAsync("completed");
        }
    }

    [Fact]
    public async Task LoginPage_ShouldDisplayGoogleLoginOption()
    {
        // Set test name for screenshot capture
        SetCurrentTestName(nameof(LoginPage_ShouldDisplayGoogleLoginOption));

        try
        {
            // Arrange
            var mainPage = new MainPage(Page!, Config.BaseUrl);
            var loginPage = new LoginPage(Page!);

            // Act
            await RetryAsync(async () =>
            {
                await mainPage.NavigateAsync();
                await mainPage.ClickLoginLinkAsync();
            });

            // Assert - Verify we're on the login page
            var isOnLoginPage = await loginPage.IsOnLoginPageAsync();
            isOnLoginPage.Should().BeTrue();

            // Assert - Verify Google login option is available
            var isGoogleVisible = await loginPage.IsGoogleLoginVisibleAsync();
            isGoogleVisible.Should().BeTrue("Google login option should be visible on login page");

            // Record test success
            RecordTestSuccess();
        }
        catch (Exception ex)
        {
            // Capture screenshot on failure
            await CaptureScreenshotAsync("failure");
            await CaptureFinalScreenshotAsync("failed");
            throw; // Re-throw to ensure test fails properly
        }
        finally
        {
            // Always capture final screenshot for debugging
            await CaptureFinalScreenshotAsync("completed");
        }
    }

}