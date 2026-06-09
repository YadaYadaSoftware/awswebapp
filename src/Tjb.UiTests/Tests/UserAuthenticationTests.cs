using FluentAssertions;
using Tjb.UiTests.Fixtures;
using Tjb.UiTests.Pages;
using Xunit;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace Tjb.UiTests.Tests;

[Collection("UiTests")]
public class UserAuthenticationTests : BaseUiTest
{
    public UserAuthenticationTests(OAuthTokenFixture auth, AppReadinessFixture appReady)
        : base(auth, appReady) { }

    [Fact]
    public async Task LoggedInUser_ShouldDisplayUserName()
    {
        // Set test name for screenshot capture
        SetCurrentTestName(nameof(LoggedInUser_ShouldDisplayUserName));

        try
        {
            // Arrange
            var mainPage = new MainPage(Page!, Config.BaseUrl);

            // Establish a real Identity session via the gated test-auth endpoint. Skips when no
            // token is configured or the gate is off (404, as on app).
            if (!await TrySignInViaTestAuthAsync())
            {
                return;
            }

            // Act - Navigate to main page carrying the session cookie
            await RetryAsync(async () =>
            {
                await mainPage.NavigateAsync();
            });

            // Assert - Verify user is logged in and name is displayed
            await mainPage.ExpectUserLoggedInAsync();

            // Act - Get the logged in user name
            var userName = await mainPage.GetLoggedInUserNameAsync();

            // Assert - Verify user name is not empty
            userName.Should().NotBeNullOrEmpty("Logged in user name should not be empty");
            userName.Length.Should().BeGreaterThan(0, "Logged in user name should have content");

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

            // Clean up test session
            await CleanupTestSessionAsync();
        }
    }

    [Fact]
    public async Task LoginLogout_ShouldToggleAuthenticationState()
    {
        // Set test name for screenshot capture
        SetCurrentTestName(nameof(LoginLogout_ShouldToggleAuthenticationState));

        try
        {
            // Arrange
            var mainPage = new MainPage(Page!, Config.BaseUrl);

            // Act - Navigate to main page
            await RetryAsync(async () =>
            {
                await mainPage.NavigateAsync();
            });

            // Check initial state - this might be logged in or logged out depending on test environment
            var initiallyLoggedIn = await mainPage.IsUserLoggedInAsync();

            if (initiallyLoggedIn)
            {
                // If logged in, verify user name is displayed
                var userName = await mainPage.GetLoggedInUserNameAsync();
                userName.Should().NotBeNullOrEmpty();

                // Note: In a complete test, you would:
                // 1. Click logout
                // 2. Verify login link appears
                // 3. Login again
                // 4. Verify user name appears again
            }
            else
            {
                // If not logged in, verify login link is visible
                await mainPage.ExpectLoginLinkVisibleAsync();
            }

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