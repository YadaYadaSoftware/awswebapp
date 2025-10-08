using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace TaskManager.UiTests;

public class TestConfiguration
{
    private static TestConfiguration? _instance;
    private static readonly object _lock = new object();

    public string BaseUrl { get; }
    public string LoginPath { get; }
    public string GoogleLoginUrl { get; }
    public int TestTimeout { get; }
    public int RetryAttempts { get; }

    // OAuth Configuration
    public bool UseMockedOAuth { get; }
    public bool EnableOAuthTesting { get; }
    public bool UseTokenBasedAuth { get; }
    public string? GoogleTestAccessToken { get; }
    public string? GoogleTestRefreshToken { get; }

    private TestConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var testSettings = configuration.GetSection("TestSettings");

        BaseUrl = testSettings["BaseUrl"] ?? "https://dev.appcloud.systems";
        LoginPath = testSettings["LoginPath"] ?? "/Identity/Account/Login";
        GoogleLoginUrl = testSettings["GoogleLoginUrl"] ?? "https://accounts.google.com/v3/signin/identifier";
        TestTimeout = int.Parse(testSettings["TestTimeout"] ?? "30000");
        RetryAttempts = int.Parse(testSettings["RetryAttempts"] ?? "3");

        // OAuth Configuration - prioritize environment variables for security
        UseMockedOAuth = bool.Parse(Environment.GetEnvironmentVariable("USE_MOCKED_OAUTH") ?? "true");
        EnableOAuthTesting = bool.Parse(Environment.GetEnvironmentVariable("ENABLE_OAUTH_TESTING") ?? "false");
        UseTokenBasedAuth = bool.Parse(Environment.GetEnvironmentVariable("USE_TOKEN_BASED_AUTH") ?? "false");

        // Note: Test credentials are no longer used - we use token-based authentication instead

        // Load token for token-based authentication
        if (UseTokenBasedAuth)
        {
            GoogleTestAccessToken = Environment.GetEnvironmentVariable("GOOGLE_TEST_ACCESS_TOKEN");
            GoogleTestRefreshToken = Environment.GetEnvironmentVariable("GOOGLE_TEST_REFRESH_TOKEN");

            // Either access token or refresh token must be provided
            if (string.IsNullOrEmpty(GoogleTestAccessToken) && string.IsNullOrEmpty(GoogleTestRefreshToken))
            {
                throw new InvalidOperationException(
                    "Token-based authentication is enabled but neither GOOGLE_TEST_ACCESS_TOKEN nor GOOGLE_TEST_REFRESH_TOKEN environment variables are set.");
            }
        }
    }

    public static TestConfiguration Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new TestConfiguration();
                    }
                }
            }
            return _instance;
        }
    }
}