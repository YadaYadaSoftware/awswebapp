using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Tjb.UiTests.Fixtures;

/// <summary>
/// Thrown when the deployed app fails to become responsive within the readiness budget.
/// </summary>
public class AppNotReadyException : Exception
{
    public AppNotReadyException(string message) : base(message) { }
}

/// <summary>
/// Suite-level app-readiness gate. Polls <c>${TEST_BASE_URL}/health</c> until it returns
/// 200 (cold-start / ALB warmup can take 30-60s after a fresh deploy) before any test runs.
///
/// Runs ONCE per suite via <c>[Collection("UiTests")]</c> / <see cref="UiTestCollection"/>;
/// individual tests do not re-poll.
/// </summary>
public class AppReadinessFixture : IAsyncLifetime
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(5);

    private readonly string _baseUrl;

    public AppReadinessFixture()
    {
        // Mirror TestConfiguration's resolution: env var wins, then the configured default.
        _baseUrl = (Environment.GetEnvironmentVariable("TEST_BASE_URL")
                    ?? TestConfiguration.Instance.BaseUrl).TrimEnd('/');
    }

    public async Task InitializeAsync()
    {
        var healthUrl = $"{_baseUrl}/health";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var start = DateTime.UtcNow;
        string lastStatus = "no response";
        var attempt = 0;

        while (DateTime.UtcNow - start < Budget)
        {
            attempt++;
            try
            {
                var response = await http.GetAsync(healthUrl);
                lastStatus = $"{(int)response.StatusCode} {response.ReasonPhrase}";
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"App ready: {healthUrl} returned {lastStatus} after {attempt} poll(s), " +
                                      $"{(DateTime.UtcNow - start).TotalSeconds:F0}s.");
                    return;
                }
            }
            catch (Exception ex)
            {
                lastStatus = ex.Message;
            }

            Console.WriteLine($"Waiting for app readiness ({healthUrl}): last status '{lastStatus}' (attempt {attempt})...");
            await Task.Delay(PollInterval);
        }

        throw new AppNotReadyException(
            $"app not ready: {healthUrl} did not return 200 within {Budget.TotalMinutes:F0} minutes " +
            $"(last status seen: '{lastStatus}', {attempt} attempts).");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
