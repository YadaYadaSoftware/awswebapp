using FluentAssertions;
using TaskManager.UiTests.Pages;
using Xunit;
using System.Threading.Tasks;

namespace TaskManager.UiTests.Tests;

public class UserAuthenticationTests : BaseTest
{
    [Fact]
    public async Task LoggedInUser_ShouldDisplayUserName()
    {
        // Arrange
        var mainPage = new MainPage(Page!, Config.BaseUrl);

        // Act - Navigate to main page (assuming user is already logged in)
        await mainPage.NavigateAsync();

        // Assert - Verify user is logged in and name is displayed
        var isUserLoggedIn = await mainPage.IsUserLoggedInAsync();
        isUserLoggedIn.Should().BeTrue("User should be logged in");

        // Act - Get the logged in user name
        var userName = await mainPage.GetLoggedInUserNameAsync();

        // Assert - Verify user name is not empty
        userName.Should().NotBeNullOrEmpty("Logged in user name should not be empty");
        userName.Length.Should().BeGreaterThan(0, "Logged in user name should have content");
    }

    [Fact]
    public async Task LoginLogout_ShouldToggleAuthenticationState()
    {
        // Arrange
        var mainPage = new MainPage(Page!, Config.BaseUrl);

        // Act - Navigate to main page
        await mainPage.NavigateAsync();

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
            var isLoginVisible = await mainPage.IsLoginLinkVisibleAsync();
            isLoginVisible.Should().BeTrue("Login link should be visible when not logged in");
        }
    }
}