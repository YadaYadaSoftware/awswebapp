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
    public async Task CompleteGoogleOAuthFlow_ShouldReturnToApplication()
    {
        // Set test name for screenshot capture
        SetCurrentTestName(nameof(CompleteGoogleOAuthFlow_ShouldReturnToApplication));

        try
        {
            // Skip test if OAuth testing is not configured
            if (!Config.EnableOAuthTesting)
            {
                Console.WriteLine("Skipping OAuth test - ENABLE_OAUTH_TESTING is not set to true");
                return; // Skip the test by returning early
            }

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

            // Complete OAuth flow with test credentials if not using mocked OAuth
            if (!Config.UseMockedOAuth)
            {
                // Act - Complete Google OAuth login with test credentials
                await RetryAsync(async () =>
                {
                    await loginPage.CompleteGoogleOAuthFlowAsync(
                        Config.TestGoogleEmail!,
                        Config.TestGooglePassword!
                    );
                });

                // Assert - Verify OAuth callback is received
                await RetryAsync(async () =>
                {
                    var isCallbackReceived = await loginPage.IsOAuthCallbackReceivedAsync();
                    isCallbackReceived.Should().BeTrue("Should receive OAuth callback with authorization code");
                });

                // Assert - Verify we're back in the application
                await RetryAsync(async () =>
                {
                    var isBackInApp = await loginPage.IsBackInApplicationAsync();
                    isBackInApp.Should().BeTrue("Should be redirected back to application after OAuth");
                });

                // Additional verification - check if user is logged in
                await RetryAsync(async () =>
                {
                    var isUserLoggedIn = await mainPage.IsUserLoggedInAsync();
                    isUserLoggedIn.Should().BeTrue("User should be logged in after OAuth flow");
                });
            }
            else
            {
                // For mocked OAuth, just verify the redirect works
                await RetryAsync(async () =>
                {
                    var isGoogleRedirect = await loginPage.IsGoogleOauthRedirectAsync();
                    isGoogleRedirect.Should().BeTrue("Should be redirected to Google OAuth");
                });
            }

            // Record test success
            RecordTestSuccess();
        }
        catch (Exception ex) when (ex.Message.Contains("OAuth testing is not enabled"))
        {
            // This is expected when OAuth testing is disabled, just return
            return;
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