using FluentAssertions;
using Tjb.UiTests.Fixtures;
using Tjb.UiTests.Pages;
using Xunit;
using System.Threading.Tasks;
using System;

namespace Tjb.UiTests.Tests;

[Collection("UiTests")]
public class LoginNavigationTests : BaseUiTest
{
    public LoginNavigationTests(OAuthTokenFixture auth, AppReadinessFixture appReady)
        : base(auth, appReady) { }

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
            await mainPage.ExpectLoginLinkVisibleAsync();

            // Act - Click the login link
            await RetryAsync(async () =>
            {
                await mainPage.ClickLoginLinkAsync();
            });

            // Assert - Verify we're redirected to the login page
            await loginPage.ExpectOnLoginPageAsync();

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
            await loginPage.ExpectOnLoginPageAsync();

            // Assert - Verify Google login option is available
            await loginPage.ExpectGoogleLoginVisibleAsync();

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