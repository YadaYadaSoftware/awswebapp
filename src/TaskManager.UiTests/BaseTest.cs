using Microsoft.Playwright;
using Xunit;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

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
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation);
        var projectRoot = Directory.GetParent(assemblyDir)?.Parent?.Parent?.Parent?.FullName ?? assemblyDir;
        var testResultsDir = Path.Combine(projectRoot, "src", "TaskManager.UiTests", "TestResults");
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

        throw new AggregateException($"Action failed after {maxAttempts} attempts", exceptions);
    }

    protected async Task CaptureScreenshotAsync(string screenshotName)
    {
        if (Page == null) return;

        try
        {
            // Get the project root directory using assembly location
            var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var assemblyDir = Path.GetDirectoryName(assemblyLocation);
            var projectRoot = Directory.GetParent(assemblyDir)?.Parent?.Parent?.Parent?.FullName ?? assemblyDir;
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to capture screenshot: {ex.Message}");
        }
    }

    protected void SetCurrentTestName(string testName)
    {
        _currentTestName = testName;
    }

    protected async Task CaptureFinalScreenshotAsync(string testStatus)
    {
        if (Page != null && _currentTestName != null)
        {
            await CaptureScreenshotAsync($"final_{testStatus}");
        }
    }
}