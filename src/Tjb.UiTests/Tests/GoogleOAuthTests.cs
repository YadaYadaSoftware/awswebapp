using FluentAssertions;
using Tjb.UiTests.Fixtures;
using Tjb.UiTests.Pages;
using Xunit;
using Xunit.Sdk;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
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

        // Establish a real Identity session via the gated test-auth endpoint (validates the
        // id_token server-side and issues a genuine Identity cookie). Skips when no token is
        // configured or the gate is off (404, as on app).
        if (!await TrySignInViaTestAuthAsync())
        {
            return;
        }

        // Act - Navigate to the app carrying the session cookie
        await RetryAsync(async () => await mainPage.NavigateAsync());

        // Assert - Verify the authenticated nav renders (Expect has built-in waiting)
        await mainPage.ExpectUserLoggedInAsync();

        // Record test success
        RecordTestSuccess();
    }
    catch (Exception)
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

    // Security/gate probe (runs on every env, including production-shaped app/test):
    // the test-auth endpoint must NEVER establish a session for a bogus id_token. When the gate is
    // ON it validates and rejects (401/400); when OFF the endpoint is absent (404). Either way it
    // must not return 200, and the app must remain anonymous.
    [Fact]
    public async Task TestAuthEndpoint_NeverEstablishesSessionForInvalidToken()
    {
        SetCurrentTestName(nameof(TestAuthEndpoint_NeverEstablishesSessionForInvalidToken));

        try
        {
            var response = await Context!.APIRequest.PostAsync($"{Config.BaseUrl.TrimEnd('/')}/test-auth/signin",
                new Microsoft.Playwright.APIRequestContextOptions
                {
                    Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer not-a-real-id-token" }
                });

            response.Status.Should().NotBe(200,
                "the test-auth endpoint must never issue a session for an invalid id_token");

            // And the app must still be anonymous (no session was established).
            var mainPage = new MainPage(Page!, Config.BaseUrl);
            await RetryAsync(async () => await mainPage.NavigateAsync());
            await mainPage.ExpectLoginLinkVisibleAsync();

            RecordTestSuccess();
        }
        catch (Exception)
        {
            await CaptureScreenshotAsync("failure");
            await CaptureFinalScreenshotAsync("failed");
            throw;
        }
        finally
        {
            await CaptureFinalScreenshotAsync("completed");
            await CleanupTestSessionAsync();
        }
    }
}