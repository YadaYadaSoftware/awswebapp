using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;

namespace TaskManager.UiTests;

public class GoogleTokenService
{
    private readonly HttpClient _httpClient;
    private readonly string? _testToken;
    private readonly string? _refreshToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private string? _cachedAccessToken;

    public GoogleTokenService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _testToken = Environment.GetEnvironmentVariable("GOOGLE_TEST_ACCESS_TOKEN");
        _refreshToken = Environment.GetEnvironmentVariable("GOOGLE_TEST_REFRESH_TOKEN");
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        // If we have a cached token and it's still valid, return it
        if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiry)
        {
            return _cachedAccessToken;
        }

        // If a static test token is provided, use it directly (no expiration)
        if (!string.IsNullOrEmpty(_testToken) && string.IsNullOrEmpty(_refreshToken))
        {
            _cachedAccessToken = _testToken;
            return _testToken;
        }

        // If we have a refresh token, use it to get a new access token
        if (!string.IsNullOrEmpty(_refreshToken))
        {
            return await RefreshAccessTokenAsync();
        }

        // No tokens available
        Console.WriteLine("No access token or refresh token provided.");
        Console.WriteLine("Please set either:");
        Console.WriteLine("1. GOOGLE_TEST_ACCESS_TOKEN (for static token)");
        Console.WriteLine("2. GOOGLE_TEST_REFRESH_TOKEN + GOOGLE_OAUTH_CLIENT_ID + GOOGLE_OAUTH_CLIENT_SECRET (for auto-refresh)");

        return null;
    }

    private async Task<string?> RefreshAccessTokenAsync()
    {
        try
        {
            var clientId = Environment.GetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_SECRET");

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                Console.WriteLine("Missing GOOGLE_OAUTH_CLIENT_ID or GOOGLE_OAUTH_CLIENT_SECRET for token refresh.");
                return null;
            }

            var tokenRequest = new
            {
                client_id = clientId,
                client_secret = clientSecret,
                refresh_token = _refreshToken,
                grant_type = "refresh_token"
            };

            var response = await _httpClient.PostAsJsonAsync("https://oauth2.googleapis.com/token", tokenRequest);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>();

            if (tokenResponse?.access_token != null)
            {
                _cachedAccessToken = tokenResponse.access_token;
                // Set expiry to 50 minutes from now (tokens typically last 1 hour, but we'll refresh early)
                _tokenExpiry = DateTime.UtcNow.AddMinutes(50);

                Console.WriteLine($"Token refreshed successfully. Expires at: {_tokenExpiry}");
                return tokenResponse.access_token;
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to refresh access token: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            // Use Google's tokeninfo endpoint to validate the token
            var response = await _httpClient.GetAsync($"https://oauth2.googleapis.com/tokeninfo?access_token={token}");

            if (response.IsSuccessStatusCode)
            {
                var tokenInfo = await response.Content.ReadFromJsonAsync<GoogleTokenInfo>();
                return tokenInfo?.aud != null; // Token is valid if it has an audience
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Token validation failed: {ex.Message}");
            return false;
        }
    }

    private class GoogleTokenResponse
    {
        public string? access_token { get; set; }
        public string? token_type { get; set; }
        public int expires_in { get; set; }
        public string? refresh_token { get; set; }
    }

    private class GoogleTokenInfo
    {
        public string? aud { get; set; }
        public string? sub { get; set; }
        public string? email { get; set; }
        public long exp { get; set; }
        public string[]? scopes { get; set; }
    }
}