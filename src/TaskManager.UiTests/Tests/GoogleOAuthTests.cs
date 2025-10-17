using FluentAssertions;
using TaskManager.UiTests.Pages;
using Xunit;
using Xunit.Sdk;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace TaskManager.UiTests.Tests;

public class GoogleOAuthTests : BaseTest
{
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
            var isOnLoginPage = await loginPage.IsOnLoginPageAsync();
            isOnLoginPage.Should().BeTrue();

            // Act - Click Google login
            await RetryAsync(async () =>
            {
                await loginPage.ClickGoogleLoginAsync();
            });

            // Assert - Verify we're redirected to Google
            await RetryAsync(async () =>
            {
                var isGoogleRedirect = await loginPage.IsGoogleOauthRedirectAsync();
                isGoogleRedirect.Should().BeTrue("Should be redirected to Google OAuth");
            });

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

        // Get Google access token
        using var httpClient = new HttpClient();
        var tokenService = new GoogleTokenService(httpClient, new ConfigurationBuilder().Build());
        var accessToken = await tokenService.GetAccessTokenAsync();

        // Skip test if no token is available
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("Skipping token-based test - no access token available");
            return;
        }

        // Validate token before using it
        var isValidToken = await tokenService.ValidateTokenAsync(accessToken);
        isValidToken.Should().BeTrue("Access token should be valid");

        // Act - Navigate to main page and click login
        await RetryAsync(async () =>
        {
            await mainPage.NavigateAsync();
            await mainPage.ClickLoginLinkAsync();
        });

        // Assert - Verify we're on the login page
        var isOnLoginPage = await loginPage.IsOnLoginPageAsync();
        isOnLoginPage.Should().BeTrue();

        // Act - Use token to authenticate directly (bypass Google OAuth flow)
        await RetryAsync(async () =>
        {
            await loginPage.AuthenticateWithTokenAsync(accessToken);
        });

        // Assert - Verify user is logged in
        await RetryAsync(async () =>
        {
            var isUserLoggedIn = await mainPage.IsUserLoggedInAsync();
            isUserLoggedIn.Should().BeTrue("User should be logged in after token authentication");
        });

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