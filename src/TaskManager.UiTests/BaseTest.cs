using Microsoft.Playwright;
using Xunit;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using TaskManager.UiTests;

namespace TaskManager.UiTests;

public class BaseTest : IAsyncLifetime
{
    protected IPlaywright? Playwright { get; private set; }
    protected IBrowser? Browser { get; private set; }
    protected IBrowserContext? Context { get; private set; }
    protected IPage? Page { get; private set; }

    protected TestConfiguration Config => TestConfiguration.Instance;
    protected TestReporter Reporter { get; private set; } = new();
    protected TestEnvironment Environment { get; private set; } = TestEnvironment.GetCurrent();
    protected TestDataManager? DataManager { get; private set; }

    private string? _currentTestName;

    public async Task InitializeAsync()
    {
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
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });

        Page = await Context.NewPageAsync();

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
            var screenshotsDir = Path.Combine(testResultsDir, "Screenshots");
            Directory.CreateDirectory(screenshotsDir);

            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"{_currentTestName}_{screenshotName}_{timestamp}.png";
            var filepath = Path.Combine(screenshotsDir, filename);

            await Page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = filepath,
                FullPage = true
            });

            Console.WriteLine($"Screenshot saved: {filepath}");
            Console.WriteLine($"Working directory: {workingDirectory}");
            Console.WriteLine($"Screenshots directory: {screenshotsDir}");
            Console.WriteLine($"Current test name: {_currentTestName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to capture screenshot: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
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
}