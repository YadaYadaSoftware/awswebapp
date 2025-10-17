# OAuth Testing Environment Setup Script
# This script prompts for OAuth testing configuration and launches VS Code

Write-Host "🔐 Google OAuth Testing Setup" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green
Write-Host ""

# Function to prompt for user input
function Get-UserInput {
    param(
        [string]$Prompt,
        [string]$Default = ""
    )

    $value = Read-Host "$Prompt"
    if ([string]::IsNullOrEmpty($value)) {
        return $Default
    }
    return $value
}

Write-Host "Configure OAuth testing environment variables:" -ForegroundColor Yellow
Write-Host ""

# Prompt for OAuth testing settings
$enableOAuthTesting = Get-UserInput "Enable OAuth testing? (true/false)" "true"
$useMockedOAuth = Get-UserInput "Use mocked OAuth? (true/false) - Recommended for development" "false"
$useTokenBasedAuth = Get-UserInput "Use token-based authentication? (true/false) - Recommended for reliable testing" "true"

$googleTestAccessToken = ""
$googleTestRefreshToken = ""
$googleOauthClientId = ""
$googleOauthClientSecret = ""

if ($useTokenBasedAuth -eq "true") {
    Write-Host ""
    Write-Host "🔑 Token-Based Authentication Setup:" -ForegroundColor Yellow
    Write-Host "=====================================" -ForegroundColor Yellow
    Write-Host "Choose one of the following options:"
    Write-Host "1. Use a static access token (expires in 1 hour)"
    Write-Host "2. Use a refresh token (auto-refreshes, recommended)"
    $tokenOption = Read-Host "Enter choice (1 or 2)"

    if ($tokenOption -eq "1") {
        $googleTestAccessToken = Get-UserInput "Google Access Token (from OAuth Playground)"
    }
    elseif ($tokenOption -eq "2") {
        $googleTestRefreshToken = Get-UserInput "Google Refresh Token (from OAuth Playground)"
        $googleOauthClientId = Get-UserInput "Google OAuth Client ID (from Google Cloud Console)"
        $googleOauthClientSecret = Get-UserInput "Google OAuth Client Secret (from Google Cloud Console)"
    }
}

Write-Host ""
Write-Host "📋 Configuration Summary:" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host "ENABLE_OAUTH_TESTING = $enableOAuthTesting"
Write-Host "USE_MOCKED_OAUTH = $useMockedOAuth"
Write-Host "USE_TOKEN_BASED_AUTH = $useTokenBasedAuth"

if ($useTokenBasedAuth -eq "true") {
    if ($googleTestAccessToken) {
        Write-Host "GOOGLE_TEST_ACCESS_TOKEN = $( "*" * $googleTestAccessToken.Length )"
    }
    if ($googleTestRefreshToken) {
        Write-Host "GOOGLE_TEST_REFRESH_TOKEN = $( "*" * $googleTestRefreshToken.Length )"
    }
    if ($googleOauthClientId) {
        Write-Host "GOOGLE_OAUTH_CLIENT_ID = $( "*" * $googleOauthClientId.Length )"
    }
    if ($googleOauthClientSecret) {
        Write-Host "GOOGLE_OAUTH_CLIENT_SECRET = $( "*" * $googleOauthClientSecret.Length )"
    }
}

$confirmation = Read-Host "`nProceed with this configuration? (y/n)"
if ($confirmation -ne 'y' -and $confirmation -ne 'Y') {
    Write-Host "Setup cancelled." -ForegroundColor Red
    exit
}

Write-Host ""
Write-Host "🔧 Setting environment variables..." -ForegroundColor Yellow

try {
    # Set environment variables for current session
    $env:ENABLE_OAUTH_TESTING = $enableOAuthTesting
    $env:USE_MOCKED_OAUTH = $useMockedOAuth
    $env:USE_TOKEN_BASED_AUTH = $useTokenBasedAuth

    if ($useTokenBasedAuth -eq "true") {
        if ($googleTestAccessToken) {
            $env:GOOGLE_TEST_ACCESS_TOKEN = $googleTestAccessToken
        }

        if ($googleTestRefreshToken) {
            $env:GOOGLE_TEST_REFRESH_TOKEN = $googleTestRefreshToken
        }

        if ($googleOauthClientId) {
            $env:GOOGLE_OAUTH_CLIENT_ID = $googleOauthClientId
        }

        if ($googleOauthClientSecret) {
            $env:GOOGLE_OAUTH_CLIENT_SECRET = $googleOauthClientSecret
        }
    }

    Write-Host "✅ Environment variables set successfully!" -ForegroundColor Green

    # Set permanent environment variables for future sessions
    Write-Host ""
    Write-Host "💾 Setting permanent environment variables..." -ForegroundColor Yellow

    [Environment]::SetEnvironmentVariable("ENABLE_OAUTH_TESTING", $enableOAuthTesting, "User")
    [Environment]::SetEnvironmentVariable("USE_MOCKED_OAUTH", $useMockedOAuth, "User")
    [Environment]::SetEnvironmentVariable("USE_TOKEN_BASED_AUTH", $useTokenBasedAuth, "User")

    if ($useTokenBasedAuth -eq "true") {
        if ($googleTestAccessToken) {
            [Environment]::SetEnvironmentVariable("GOOGLE_TEST_ACCESS_TOKEN", $googleTestAccessToken, "User")
        }

        if ($googleTestRefreshToken) {
            [Environment]::SetEnvironmentVariable("GOOGLE_TEST_REFRESH_TOKEN", $googleTestRefreshToken, "User")
        }

        if ($googleOauthClientId) {
            [Environment]::SetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_ID", $googleOauthClientId, "User")
        }

        if ($googleOauthClientSecret) {
            [Environment]::SetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_SECRET", $googleOauthClientSecret, "User")
        }
    }

    Write-Host "✅ Permanent environment variables set!" -ForegroundColor Green

    Write-Host ""
    Write-Host "🚀 Starting VS Code..." -ForegroundColor Green

    # Start VS Code
    Start-Process "code" -ArgumentList "."

    Write-Host ""
    Write-Host "✅ VS Code started!" -ForegroundColor Green
    Write-Host ""
    Write-Host "🔑 Getting OAuth Credentials:" -ForegroundColor Cyan
    Write-Host "   - Go to Google Cloud Console: https://console.cloud.google.com/"
    Write-Host "   - Create a new project or select existing one"
    Write-Host "   - Go to 'APIs & Credentials' > 'Credentials'"
    Write-Host "   - Click 'Create Credentials' > 'OAuth 2.0 Client IDs'"
    Write-Host "   - Configure OAuth consent screen if prompted"
    Write-Host "   - Copy Client ID and Client Secret for refresh token flow"
    Write-Host ""
    Write-Host " Tips:" -ForegroundColor Cyan
    Write-Host "   - Run 'dotnet test --filter TokenBasedGoogleLogin_ShouldAuthenticateWithValidToken' to test token-based auth"
    Write-Host "   - Run 'dotnet test --filter GoogleLogin_ShouldRedirectToGoogle' to test UI redirect"
    Write-Host "   - Check the Test Explorer in VS Code for all OAuth tests"
    Write-Host "   - Use token-based auth (USE_TOKEN_BASED_AUTH=true) for most reliable testing"
    Write-Host "   - Use mocked OAuth (USE_MOCKED_OAUTH=true) for fastest development testing"
    Write-Host ""
    Write-Host "🔑 Token-Based Authentication Setup:" -ForegroundColor Cyan
    Write-Host "   - Get tokens from: https://developers.google.com/oauthplayground"
    Write-Host "   - Select scopes: openid, profile, email"
    Write-Host "   - Use refresh tokens for long-term automated testing"
    Write-Host "   - Get OAuth credentials from: https://console.cloud.google.com/"
    Write-Host ""
    Write-Host " To modify settings later:" -ForegroundColor Cyan
    Write-Host "   - Run this script again"
    Write-Host "   - Or set environment variables manually in your system settings"
    Write-Host "   - Or modify them directly in VS Code terminal"

}
catch {
    Write-Host ""
    Write-Host "❌ Error setting environment variables: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "1. Make sure you have administrator privileges if setting system-wide variables"
    Write-Host "2. Try running PowerShell as Administrator"
    Write-Host "3. Check if VS Code is already running and restart it"
}