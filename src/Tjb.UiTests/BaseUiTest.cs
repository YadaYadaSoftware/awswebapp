using Microsoft.Playwright;
using Xunit;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Tjb.UiTests;
using Tjb.UiTests.Fixtures;

namespace Tjb.UiTests;

/// <summary>
/// Base class for every UI test. Wires in the suite-level <see cref="OAuthTokenFixture"/>
/// (fresh Google token) and <see cref="AppReadinessFixture"/> (app warmed up), and gives
/// each test a <see cref="Page"/> with timeouts sized for SSR + ALB latency and the OAuth
/// token pre-applied. Derived classes must carry <c>[Collection("UiTests")]</c> and forward
/// the two fixtures through their constructor.
/// </summary>
public abstract class BaseUiTest : IAsyncLifetime
{
    protected IPlaywright? Playwright { get; private set; }
    protected IBrowser? Browser { get; private set; }
    protected IBrowserContext? Context { get; private set; }
    protected IPage? Page { get; private set; }

    protected OAuthTokenFixture Auth { get; }
    protected AppReadinessFixture AppReady { get; }

    protected TestConfiguration Config => TestConfiguration.Instance;
    protected TestReporter Reporter { get; private set; } = new();
    protected TestEnvironment Environment { get; private set; } = TestEnvironment.GetCurrent();
    protected TestDataManager? DataManager { get; private set; }

    /// <summary>The shared, suite-fresh Google access token (null when no refresh token is configured).</summary>
    protected string? AccessToken => Auth?.AccessToken;

    private string? _currentTestName;

    protected BaseUiTest(OAuthTokenFixture auth, AppReadinessFixture appReady)
    {
        Auth = auth;
        AppReady = appReady;
    }

    public async Task InitializeAsync()
    {
        // Defensive refresh: a long suite could push the token past ~50min between
        // collection start and this test. No-ops when the token is still fresh.
        await Auth.RefreshIfStaleAsync();

        // Configure test environment
        Environment.ConfigureTestSettings();

        // Install Playwright browsers if not already installed
        Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" }
        });

        Context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
            // Configure context for better OAuth testing
            IgnoreHTTPSErrors = true // Handle self-signed certificates in development
        });

        // Capture a Playwright trace (DOM snapshots, network, console) so a failed
        // UI test has a downloadable trace.zip artifact (see test-summary-reporting).
        await Context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });

        Page = await Context.NewPageAsync();

        // Sensible defaults so no test re-derives them: 30s covers SSR + ALB request
        // time; 60s navigation supports cold-context first navigation.
        Page.SetDefaultTimeout(30_000);
        Page.SetDefaultNavigationTimeout(60_000);

        // Apply the shared OAuth token so authenticated pages don't redirect to sign-in.
        await ApplyAuthCookieAsync();

        // Initialize test data manager
        DataManager = new TestDataManager(Context!, Environment);

        // Ensure test environment is ready
        await DataManager.EnsureTestUserExistsAsync();

        // Ensure TestResults directory structure exists
        var workingDirectory = System.Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? Directory.GetCurrentDirectory();
        var testResultsDir = Path.Combine(workingDirectory, "src", "TaskManager.UiTests", "TestResults");
        var screenshotsDir = Path.Combine(testResultsDir, "Screenshots");
        Directory.CreateDirectory(screenshotsDir);
    }

    /// <summary>
    /// Applies the shared OAuth access token to the browser context as the ASP.NET Identity
    /// application cookie, so navigating to an authenticated page does not redirect to sign-in.
    /// No-op when no token is configured (local dev).
    /// </summary>
    protected async Task ApplyAuthCookieAsync()
    {
        if (Context == null || string.IsNullOrEmpty(AccessToken))
        {
            return;
        }

        await Context.AddCookiesAsync(new[]
        {
            new Cookie
            {
                Name = ".AspNetCore.Identity.Application",
                Value = AccessToken!,
                Domain = new Uri(Config.BaseUrl).Host,
                Path = "/"
            }
        });
    }

    public async Task DisposeAsync()
    {
        try
        {
            // Cleanup test data
            if (DataManager != null)
            {
                await DataManager.CleanupTestDataAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error during test cleanup: {ex.Message}");
        }
        finally
        {
            // Always cleanup browser resources
            if (Page != null)
            {
                await Page.CloseAsync();
            }

            if (Context != null)
            {
                await StopTracingAsync();
                await Context.CloseAsync();
            }

            if (Browser != null)
            {
                await Browser.CloseAsync();
            }

            Playwright?.Dispose();

            // Generate test report
            Reporter.GenerateReport();
        }
    }

    protected async Task RetryAsync(Func<Task> action, int maxAttempts = 3)
    {
        var exceptions = new List<Exception>();

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);

                // Capture screenshot on failure for investigation
                if (Page != null && _currentTestName != null)
                {
                    await CaptureScreenshotAsync($"failure_attempt_{attempt}");
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(1000 * attempt); // Exponential backoff
                }
            }
        }

        // Record test failure in reporter with screenshot info
        if (_currentTestName != null)
        {
            var className = this.GetType().Name;
            var baseMessage = $"Action failed after {maxAttempts} attempts: {exceptions.Last().Message}";
            var screenshotInfo = "Screenshots were captured for debugging.";
            var fullMessage = $"{baseMessage} ({screenshotInfo})";
            Reporter.RecordTestEnd(_currentTestName, className, TestStatus.Failed, fullMessage);
        }

        throw new AggregateException($"Action failed after {maxAttempts} attempts", exceptions);
    }

    protected async Task CaptureScreenshotAsync(string screenshotName)
    {
        if (Page == null) return;

        try
        {
            // Use GITHUB_WORKSPACE for GitHub Actions, fallback to project root for local
            var workingDirectory = System.Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
            if (string.IsNullOrEmpty(workingDirectory))
            {
                // For local development, find the project root by looking for the .csproj file
                var currentDir = Directory.GetCurrentDirectory();

                // If we're in bin/Debug/net8.0, go up to find the project root
                if (currentDir.Contains("bin" + Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar + "net8.0"))
                {
                    workingDirectory = currentDir.Substring(0, currentDir.IndexOf("bin" + Path.DirectorySeparatorChar + "Debug"));
                }
                else if (currentDir.Contains("src" + Path.DirectorySeparatorChar + "TaskManager.UiTests"))
                {
                    workingDirectory = currentDir.Substring(0, currentDir.IndexOf("src" + Path.DirectorySeparatorChar + "TaskManager.UiTests"));
                }
                else
                {
                    workingDirectory = currentDir;
                }
            }

            // Ensure we're using the correct project root path
            var projectRoot = workingDirectory.TrimEnd(Path.DirectorySeparatorChar);

            // Remove the extra "src\TaskManager.UiTests" if it exists in the path
            if (projectRoot.EndsWith("src" + Path.DirectorySeparatorChar + "TaskManager.UiTests"))
            {
                projectRoot = projectRoot.Substring(0, projectRoot.Length - ("src" + Path.DirectorySeparatorChar + "TaskManager.UiTests").Length);
            }

            var testResultsDir = Path.Combine(projectRoot, "src", "TaskManager.UiTests", "TestResults");
            var screenshotsBaseDir = Path.Combine(testResultsDir, "Screenshots");

            // Get class name and test method name for folder structure
            var className = this.GetType().Name;
            var testMethodName = _currentTestName ?? "UnknownTest";

            // Create structured folder path: Screenshots/ClassName/TestMethodName/ExceptionName/
            var structuredDir = Path.Combine(screenshotsBaseDir, className, testMethodName, screenshotName);
            Directory.CreateDirectory(structuredDir);

            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"{timestamp}.png";
            var filepath = Path.Combine(structuredDir, filename);

            await Page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = filepath,
                FullPage = true
            });

            Console.WriteLine($"Screenshot saved: {filepath}");
            Console.WriteLine($"Working directory: {workingDirectory}");
            Console.WriteLine($"Screenshots directory: {structuredDir}");
            Console.WriteLine($"Current test name: {_currentTestName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to capture screenshot: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    private async Task StopTracingAsync()
    {
        try
        {
            if (Context == null) return;

            // Resolve the repo workspace so the trace lands where the workflow's
            // "Upload UI test artifacts" step looks: src/Tjb.UiTests/test-results/.
            var workspace = System.Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
            if (string.IsNullOrEmpty(workspace))
            {
                var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
                {
                    dir = dir.Parent;
                }
                workspace = dir?.FullName ?? Directory.GetCurrentDirectory();
            }

            var traceDir = Path.Combine(workspace, "src", "Tjb.UiTests", "test-results", this.GetType().Name);
            Directory.CreateDirectory(traceDir);

            await Context.Tracing.StopAsync(new TracingStopOptions
            {
                Path = Path.Combine(traceDir, "trace.zip")
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: failed to save Playwright trace: {ex.Message}");
        }
    }

    protected void SetCurrentTestName(string testName)
    {
        _currentTestName = testName;

        // Record test start in reporter
        var className = this.GetType().Name;
        Reporter.RecordTestStart(testName, className);
    }

    protected async Task CaptureFinalScreenshotAsync(string testStatus)
    {
        if (Page != null && _currentTestName != null)
        {
            await CaptureScreenshotAsync($"final_{testStatus}");
        }
    }

    protected void RecordTestSuccess()
    {
        if (_currentTestName != null)
        {
            var className = this.GetType().Name;
            Reporter.RecordTestEnd(_currentTestName, className, TestStatus.Passed);
        }
    }

    protected async Task CleanupTestSessionAsync()
    {
        if (Page != null)
        {
            try
            {
                // Clear all browser data to ensure clean state for next test
                await Page.Context.ClearCookiesAsync();
                await Page.EvaluateAsync("localStorage.clear()");
                await Page.EvaluateAsync("sessionStorage.clear()");

                // Close any remaining popups or dialogs
                var pages = Page.Context.Pages;
                foreach (var page in pages.Skip(1)) // Skip the main page
                {
                    try
                    {
                        await page.CloseAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to close page: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error during session cleanup: {ex.Message}");
            }
        }
    }
}
