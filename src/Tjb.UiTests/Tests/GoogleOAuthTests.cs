using FluentAssertions;
using Tjb.UiTests.Fixtures;
using Tjb.UiTests.Pages;
using Xunit;
using Xunit.Sdk;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace Tjb.UiTests.Tests;

[Collection("UiTests")]
public class GoogleOAuthTests : BaseUiTest
{
    public GoogleOAuthTests(OAuthTokenFixture auth, AppReadinessFixture appReady)
        : base(auth, appReady) { }

    [Fact]
    public async Task GoogleLogin_ShouldRedirectToGoogle()
    {
        // Set test name for screenshot capture
        SetCurrentTestName(nameof(GoogleLogin_ShouldRedirectToGoogle));

        try
        {
            // Arrange
            var mainPage = new MainPage(Page!, Config.BaseUrl);
            var loginPage = new LoginPage(Page!);

            // Act - Navigate to main page and click login
            await RetryAsync(async () =>
            {
                await mainPage.NavigateAsync();
                await mainPage.ClickLoginLinkAsync();
            });

            // Assert - Verify we're on the login page
            await loginPage.ExpectOnLoginPageAsync();

            // Act - Click Google login
            await RetryAsync(async () =>
            {
                await loginPage.ClickGoogleLoginAsync();
            });

            // Assert - Verify we're redirected to Google (Expect has built-in waiting)
            await loginPage.ExpectGoogleOauthRedirectAsync();

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
public async Task TokenBasedGoogleLogin_ShouldAuthenticateWithValidToken()
{
    // Set test name for screenshot capture
    SetCurrentTestName(nameof(TokenBasedGoogleLogin_ShouldAuthenticateWithValidToken));

    try
    {
        // Arrange
        var mainPage = new MainPage(Page!, Config.BaseUrl);
        var loginPage = new LoginPage(Page!);

        // Use the shared, suite-fresh Google access token from the OAuthTokenFixture
        // (minted at suite start / refreshed on staleness) rather than reading env directly.
        var accessToken = AccessToken;

        // Skip test if no token is available
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("Skipping token-based test - no access token available");
            return;
        }

        // Act - Navigate to main page and click login
        await RetryAsync(async () =>
        {
            await mainPage.NavigateAsync();
            await mainPage.ClickLoginLinkAsync();
        });

        // Assert - Verify we're on the login page
        await loginPage.ExpectOnLoginPageAsync();

        // Act - Use token to authenticate directly (bypass Google OAuth flow)
        await RetryAsync(async () =>
        {
            await loginPage.AuthenticateWithTokenAsync(accessToken);
        });

        // Assert - Verify user is logged in (Expect has built-in waiting)
        await mainPage.ExpectUserLoggedInAsync();

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
}