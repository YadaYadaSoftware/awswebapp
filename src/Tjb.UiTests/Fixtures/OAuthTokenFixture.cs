using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Tjb.UiTests.Fixtures;

/// <summary>
/// Thrown when the OAuth bootstrap (refresh-token exchange) fails. Surfaces the
/// upstream Google error so a failed suite reads as "auth bootstrap failed: &lt;reason&gt;"
/// rather than a cascade of misleading per-test sign-in assertions.
/// </summary>
public class AuthBootstrapException : Exception
{
    public AuthBootstrapException(string message) : base(message) { }
    public AuthBootstrapException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Suite-level OAuth token fixture. Mints a fresh Google access token from the
/// long-lived refresh token at suite start and re-mints on staleness, so no test
/// hits the ~1h access-token wall mid-suite.
///
/// Shared once per suite via <c>[Collection("UiTests")]</c> / <see cref="UiTestCollection"/>.
/// </summary>
public class OAuthTokenFixture : IAsyncLifetime
{
    // CI provides the credentials under the GOOGLE_OAUTH_* env var names
    // (see .github/workflows/deploy.yml); the task text's GOOGLE_CLIENT_* are the
    // underlying secret names. Read the OAuth-prefixed names, fall back to the
    // bare names so either wiring works.
    private static readonly string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private readonly string? _refreshToken;
    private readonly string? _clientId;
    private readonly string? _clientSecret;

    /// <summary>The current Google access token, or null when no refresh token is configured (local dev).</summary>
    public string? AccessToken { get; private set; }

    /// <summary>UTC timestamp of the most recent successful refresh.</summary>
    public DateTime AcquiredAtUtc { get; private set; } = DateTime.MinValue;

    public OAuthTokenFixture()
    {
        _refreshToken = Environment.GetEnvironmentVariable("GOOGLE_TEST_REFRESH_TOKEN");
        _clientId = Environment.GetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_ID")
                    ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
        _clientSecret = Environment.GetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_SECRET")
                        ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
    }

    public async Task InitializeAsync()
    {
        // No refresh token configured (e.g. local dev without secrets): leave the
        // token null and let token-gated tests skip, matching prior behaviour.
        // Only a CONFIGURED-but-FAILING refresh aborts the suite.
        if (string.IsNullOrEmpty(_refreshToken))
        {
            return;
        }

        await RefreshAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask; // tokens are short-lived; nothing to clean up

    /// <summary>
    /// Re-mint the access token if it is older than <paramref name="maxAgeMinutes"/>.
    /// Called from <see cref="BaseUiTest"/> before each test so long suites never
    /// run a test against a token nearing the 1-hour expiry.
    /// </summary>
    public async Task RefreshIfStaleAsync(int maxAgeMinutes = 50)
    {
        if (string.IsNullOrEmpty(_refreshToken))
        {
            return;
        }

        var age = DateTime.UtcNow - AcquiredAtUtc;
        if (age.TotalMinutes >= maxAgeMinutes)
        {
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
        {
            throw new AuthBootstrapException(
                "auth bootstrap failed: GOOGLE_TEST_REFRESH_TOKEN is set but GOOGLE_OAUTH_CLIENT_ID / " +
                "GOOGLE_OAUTH_CLIENT_SECRET are missing — cannot exchange the refresh token.");
        }

        using var http = new HttpClient();
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(TokenEndpoint, new
            {
                client_id = _clientId,
                client_secret = _clientSecret,
                refresh_token = _refreshToken,
                grant_type = "refresh_token"
            });
        }
        catch (Exception ex)
        {
            throw new AuthBootstrapException($"auth bootstrap failed: could not reach {TokenEndpoint}: {ex.Message}", ex);
        }

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            // Google returns { "error": "...", "error_description": "..." } on failure.
            throw new AuthBootstrapException(
                $"auth bootstrap failed: Google token endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        TokenResponse? token;
        try
        {
            token = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(body);
        }
        catch (Exception ex)
        {
            throw new AuthBootstrapException($"auth bootstrap failed: could not parse token response: {body}", ex);
        }

        if (token?.access_token is null)
        {
            throw new AuthBootstrapException($"auth bootstrap failed: token response contained no access_token: {body}");
        }

        AccessToken = token.access_token;
        AcquiredAtUtc = DateTime.UtcNow;
    }

    private sealed class TokenResponse
    {
        public string? access_token { get; set; }
        public string? token_type { get; set; }
        public int expires_in { get; set; }
    }
}
