using FluentAssertions;
using TaskManager.UiTests.Pages;
using Xunit;
using System.Threading.Tasks;

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
    }
}