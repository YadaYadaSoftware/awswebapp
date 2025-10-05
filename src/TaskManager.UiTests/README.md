# TaskManager UI Tests

Automated UI tests for the TaskManager application focusing on authentication and login flows.

## Test Coverage

- **Login Navigation**: Tests that clicking "Login" navigates to the correct login page
- **Google OAuth Flow**: Tests that clicking "Google" redirects to Google OAuth
- **User Authentication**: Tests that logged-in users display their name correctly

## Setup

1. Ensure Playwright browsers are installed:
   ```bash
   dotnet build
   ```

2. The tests are configured to run against the deployed environment at `https://dev.appcloud.systems`

## Running Tests

### From the UI Tests directory:
```bash
dotnet test
```

### From the solution root:
```bash
dotnet test src/TaskManager.UiTests/TaskManager.UiTests.csproj
```

## Configuration

Test settings can be modified in `appsettings.json`:

- `BaseUrl`: The base URL of the application under test
- `LoginPath`: The path to the login page
- `GoogleLoginUrl`: The expected Google OAuth URL
- `TestTimeout`: Timeout for individual test steps (milliseconds)
- `RetryAttempts`: Number of retry attempts for flaky operations

## Test Architecture

- **Page Object Model**: Each page (MainPage, LoginPage) has its own class with relevant methods
- **BaseTest**: Common setup and teardown for all tests
- **Configuration**: Centralized test configuration management
- **Retry Logic**: Built-in retry mechanism for handling flaky UI operations

## Screenshot Capture

The test framework automatically captures screenshots when tests fail to help with investigation:

- **Automatic Screenshots**: Captured on test failures and retry attempts
- **Full Page Screenshots**: Includes the complete page content for better debugging
- **Organized Storage**: Saved in `TestResults/Screenshots/` with descriptive names
- **GitHub Artifacts**: Screenshots are included in test result artifacts for download

Screenshot files are named using the pattern: `{TestName}_{failureType}_{timestamp}.png`

## Notes

- Tests run in headless Chromium browser
- Tests are designed to be run against the deployed environment
- For complete OAuth flow testing, test Google credentials would need to be configured
- Tests include proper wait conditions and error handling
- Failed tests include screenshot references in test reports