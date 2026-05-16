using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Tjb.UiTests;

public class TestReporter
{
    private readonly string _outputDirectory;
    private readonly List<TestResult> _results;

    public TestReporter()
    {
        var workingDirectory = System.Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? Directory.GetCurrentDirectory();
        _outputDirectory = Path.Combine(workingDirectory, "src", "TaskManager.UiTests", "TestResults");
        _results = new List<TestResult>();

        // Ensure output directory exists
        Directory.CreateDirectory(_outputDirectory);
    }

    public void RecordTestStart(string testName, string className)
    {
        _results.Add(new TestResult
        {
            TestName = testName,
            ClassName = className,
            Status = TestStatus.Running,
            StartTime = DateTime.UtcNow
        });
    }

    public void RecordTestEnd(string testName, string className, TestStatus status, string? errorMessage = null, string? stackTrace = null)
    {
        var result = _results.FirstOrDefault(r => r.TestName == testName && r.ClassName == className);
        if (result != null)
        {
            result.Status = status;
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            result.ErrorMessage = errorMessage;
            result.StackTrace = stackTrace;

            // Add screenshot information for failed tests
            if (status == TestStatus.Failed)
            {
                var workingDirectory = System.Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? Directory.GetCurrentDirectory();
                var screenshotsBaseDir = Path.Combine(workingDirectory, "src", "TaskManager.UiTests", "TestResults", "Screenshots");

                var screenshotFiles = new List<string>();

                if (Directory.Exists(screenshotsBaseDir))
                {
                    // Look for screenshots in the structured folder hierarchy: Screenshots/ClassName/TestName/ExceptionName/
                    var classDir = Path.Combine(screenshotsBaseDir, className);
                    if (Directory.Exists(classDir))
                    {
                        var testDir = Path.Combine(classDir, testName);
                        if (Directory.Exists(testDir))
                        {
                            // Get all PNG files from all exception subdirectories
                            var exceptionDirs = Directory.GetDirectories(testDir);
                            foreach (var exceptionDir in exceptionDirs)
                            {
                                var pngFiles = Directory.GetFiles(exceptionDir, "*.png");
                                screenshotFiles.AddRange(pngFiles.Select(Path.GetFileName));
                            }
                        }
                    }

                    // Fallback: also check for old flat structure files for backward compatibility
                    var oldFormatFiles = Directory.GetFiles(screenshotsBaseDir, $"{testName}_*.png", SearchOption.TopDirectoryOnly);
                    screenshotFiles.AddRange(oldFormatFiles.Select(Path.GetFileName));
                }

                result.ScreenshotFiles = screenshotFiles.Where(f => f != null).ToList()!;

                // Enhance error message with screenshot information
                if (result.ScreenshotFiles.Any() && !string.IsNullOrEmpty(errorMessage))
                {
                    var screenshotLinks = string.Join(", ", result.ScreenshotFiles.Select(f => $"[Screenshot: {f}]({f})"));
                    result.ErrorMessage = $"{errorMessage}\n\n**Screenshots:** {screenshotLinks}";
                }
            }
        }
    }

    public void GenerateReport()
    {
        var report = new TestReport
        {
            GeneratedAt = DateTime.UtcNow,
            Environment = TestEnvironment.GetCurrent().Name,
            Summary = new TestSummary
            {
                Total = _results.Count,
                Passed = _results.Count(r => r.Status == TestStatus.Passed),
                Failed = _results.Count(r => r.Status == TestStatus.Failed),
                Skipped = _results.Count(r => r.Status == TestStatus.Skipped)
            },
            Results = _results
        };

        var jsonReport = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var reportFile = Path.Combine(_outputDirectory, $"ui-test-report-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}.json");
        File.WriteAllText(reportFile, jsonReport);

        // Also generate a simple text summary
        GenerateTextSummary(report);
    }

    private void GenerateTextSummary(TestReport report)
    {
        var summaryFile = Path.Combine(_outputDirectory, "test-summary.txt");
        var summary = $@"
UI Test Execution Summary
Generated: {report.GeneratedAt}
Environment: {report.Environment}

Results:
- Total Tests: {report.Summary.Total}
- Passed: {report.Summary.Passed}
- Failed: {report.Summary.Failed}
- Skipped: {report.Summary.Skipped}

Success Rate: {GetSuccessRate(report.Summary) * 100:F1}%

Failed Tests:
{string.Join(Environment.NewLine, report.Results.Where(r => r.Status == TestStatus.Failed).Select(r => $"- {r.ClassName}.{r.TestName}: {r.ErrorMessage}" + (r.ScreenshotFiles.Any() ? $" (Screenshots: {string.Join(", ", r.ScreenshotFiles)})" : "")))}

Screenshots:
{string.Join(Environment.NewLine, report.Results.Where(r => r.ScreenshotFiles.Any()).SelectMany(r => r.ScreenshotFiles.Select(f => $"- {r.ClassName}.{r.TestName}: {f}")))}
";

        File.WriteAllText(summaryFile, summary);
    }

    private double GetSuccessRate(TestSummary summary)
    {
        return summary.Total > 0 ? (double)summary.Passed / summary.Total : 0;
    }
}

public class TestReport
{
    public DateTime GeneratedAt { get; set; }
    public string Environment { get; set; } = string.Empty;
    public TestSummary Summary { get; set; } = new();
    public List<TestResult> Results { get; set; } = new();
}

public class TestSummary
{
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
}

public class TestResult
{
    public string TestName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public TestStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public List<string> ScreenshotFiles { get; set; } = new();
}

public enum TestStatus
{
    Running,
    Passed,
    Failed,
    Skipped
}