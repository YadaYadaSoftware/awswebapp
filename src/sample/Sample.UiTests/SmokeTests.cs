using Microsoft.Playwright;
using Xunit;

namespace Sample.UiTests;

/// <summary>
/// Minimal post-deployment smoke against the deployed sample URL. The reusable deploy.yml runs
/// this project (via the `ui-tests-project-path` input) after deploying, with TEST_BASE_URL set to
/// https://&lt;branch-leaf&gt;.sample.appcloud.systems.
///
/// NOTE (sample-ci-deploy §5): this is a deliberately small smoke. Full token-based Google auth +
/// authenticated Guestbook coverage mirrors Tjb.UiTests (OAuthTokenFixture / AppReadinessFixture /
/// page objects) and can be lifted in once the sample is actually deploying — it is deferred here
/// because it can only run against a live deployed URL.
/// </summary>
public class SmokeTests
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("TEST_BASE_URL") ?? "https://dev.sample.appcloud.systems";

    [Fact]
    public async Task Home_LoadsAndExposesLogin()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();

        var response = await page.GotoAsync(BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.NotNull(response);
        Assert.True(response!.Ok, $"Expected a successful response from {BaseUrl}, got {response.Status}.");

        // The framework login surface should be reachable from the deployed sample.
        var content = await page.ContentAsync();
        Assert.Contains("og in", content); // matches "Log in" / "log in" without case assumptions
    }
}
