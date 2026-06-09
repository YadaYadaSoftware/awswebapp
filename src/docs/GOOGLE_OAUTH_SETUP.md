# Google OAuth Setup Guide

## Overview
This guide explains how to set up Google OAuth authentication for the application. All real authentication lives in `Tjb.Web` (ASP.NET Identity + Google OAuth); `Tjb.Api`'s auth is a no-op.

## Prerequisites
- Google Cloud Console account
- Access to create OAuth 2.0 credentials

## Step 1: Create Google Cloud Project

1. Go to the [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select an existing one
3. Enable the Google+ API (for user profile information)

## Step 2: Configure OAuth Consent Screen

1. Navigate to **APIs & Services** > **OAuth consent screen**
2. Choose **External** user type (unless you have a Google Workspace account)
3. Fill in the required information:
   - **App name**: TaskManager
   - **User support email**: Your email address
   - **Developer contact information**: Your email address
4. Add scopes:
   - `openid`
   - `profile` 
   - `email`
5. Save and continue

## Step 3: Create OAuth 2.0 Credentials

1. Navigate to **APIs & Services** > **Credentials**
2. Click **Create Credentials** > **OAuth 2.0 Client IDs**
3. Choose **Web application** as the application type
4. Configure the settings:
   - **Name**: TaskManager Web App
   - **Authorized JavaScript origins**:
     - `https://localhost:7162` (for HTTPS local development)
     - `http://localhost:5071` (for HTTP local development)
     - Add your production domain when ready
   - **Authorized redirect URIs**:
     - `https://localhost:7162/signin-google` (for HTTPS local development)
     - `http://localhost:5071/signin-google` (for HTTP local development)
     - Add your production callback URLs when ready

5. Click **Create**
6. Copy the **Client ID** and **Client Secret**

## Step 3.5: OAuth Configuration in CI

The single OAuth client is supplied to the deploy workflow via two repository secrets (Settings → Secrets and variables → Actions):

- `GOOGLE_CLIENT_ID` - Your Google OAuth 2.0 Client ID
- `GOOGLE_CLIENT_SECRET` - Your Google OAuth 2.0 Client Secret

The workflow passes these into the application stack so the deployed `Tjb.Web` container can perform the OAuth flow. There is no service-account key or Google Cloud project secret involved.

## Step 3.6: Production Redirect URIs

The app is deployed behind an ALB on a stable, domain-derived URL — there is no API Gateway. Each branch deploys to `https://{branch-leaf}.{DOMAIN_NAME}` (e.g. `https://dev.appcloud.systems`), so the redirect URIs are predictable and only need to be set once per environment.

1. **Determine the deployed URL**: `https://{branch-leaf}.{DOMAIN_NAME}` — for the `dev` branch on domain `appcloud.systems` this is `https://dev.appcloud.systems`.

2. **Update Google Cloud Console**:
   - Go to **APIs & Services** > **Credentials**
   - Edit your OAuth 2.0 Client ID
   - Add to **Authorized redirect URIs**:
     - `https://{branch-leaf}.{DOMAIN_NAME}/signin-google` (e.g. `https://dev.appcloud.systems/signin-google`)
   - Add to **Authorized JavaScript origins**:
     - `https://{branch-leaf}.{DOMAIN_NAME}` (e.g. `https://dev.appcloud.systems`)

3. Repeat for each environment you deploy (`app`, `test`, `dev`, and any feature branches you need to test against).

## Step 4: Configure Application Settings

### For Local Development

#### Option 1: User Secrets (Recommended)
```bash
# The Web project is the live application that performs OAuth
cd src/Tjb.Web

# Set the Google OAuth credentials
dotnet user-secrets set "Authentication:Google:ClientId" "your-google-client-id-here"
dotnet user-secrets set "Authentication:Google:ClientSecret" "your-google-client-secret-here"
```

**Note**: The projects have been configured with `UserSecretsId` properties to enable user secrets functionality.

#### Option 2: appsettings.Development.json (Less Secure)
Add to `src/Tjb.Web/appsettings.Development.json`:

```json
{
  "Authentication": {
    "Google": {
      "ClientId": "your-google-client-id-here",
      "ClientSecret": "your-google-client-secret-here"
    }
  }
}
```

**⚠️ Warning**: Never commit secrets to version control!

### For Production (AWS)

Use AWS Secrets Manager or environment variables:

```bash
# Environment variables
export Authentication__Google__ClientId="your-google-client-id-here"
export Authentication__Google__ClientSecret="your-google-client-secret-here"
```

## Step 5: Test the Authentication

1. Start the application:
   ```bash
   # Web is the live application that handles Google OAuth
   dotnet run --project src/Tjb.Web
   ```

2. Navigate to the web application (usually `https://localhost:7001`)
3. Click the "Login with Google" button
4. Complete the Google OAuth flow
5. Verify you're redirected back and logged in

## Authentication Surface

Authentication is handled entirely by `Tjb.Web` via ASP.NET Identity + the Google OAuth middleware. The Google callback is the standard `/signin-google` endpoint registered by the middleware; the external-login flow lives under `src/Tjb.Web/Areas/Identity/Pages/Account/`. (The `AuthController` in `Tjb.Api` is intentionally a no-op — "authentication disabled" — and is not the deployed front door.)

## Blazor Components

The Web project includes:

- `LoginDisplay.razor` - Shows login/logout UI
- Login/Logout pages for handling authentication flow

## Security Considerations

1. **HTTPS Required**: Google OAuth requires HTTPS in production
2. **Secure Storage**: Use user secrets for development, AWS Secrets Manager for production
3. **Redirect URI Validation**: Ensure redirect URIs match exactly in Google Console
4. **Scope Limitation**: Only request necessary scopes (openid, profile, email)

## Troubleshooting

### Common Issues

1. **"redirect_uri_mismatch" Error**
    - Verify redirect URIs in Google Console match your application URLs exactly
    - Check for trailing slashes and protocol (http vs https)
    - **For production deployments**: ensure `https://{branch-leaf}.{DOMAIN_NAME}/signin-google` is registered in Google Console
    - **Example**: `https://dev.appcloud.systems/signin-google`

2. **"invalid_client" Error**
   - Verify Client ID and Client Secret are correct
   - Check that credentials are properly configured in your application

3. **"access_denied" Error**
   - User declined authorization
   - Check OAuth consent screen configuration

4. **Local Development Issues**
   - Ensure you're using HTTPS for local development
   - Verify localhost URLs are added to authorized origins

### Debug Steps

1. Check application logs for detailed error messages
2. Verify Google Cloud Console configuration
3. Test with a simple OAuth flow first
4. Use browser developer tools to inspect network requests

## Production Deployment OAuth Fix

### Quick Fix for a Deployment

1. **Determine the deployed URL** for the branch: `https://{branch-leaf}.{DOMAIN_NAME}` (e.g. `https://dev.appcloud.systems`).

2. **Update Google Cloud Console**:
   - Go to [Google Cloud Console](https://console.cloud.google.com/)
   - Navigate to **APIs & Services** > **Credentials**
   - Edit your OAuth 2.0 Client ID
   - Add these **Authorized redirect URIs**:
     - `https://{branch-leaf}.{DOMAIN_NAME}/signin-google` (e.g. `https://dev.appcloud.systems/signin-google`)
   - Add these **Authorized JavaScript origins**:
     - `https://{branch-leaf}.{DOMAIN_NAME}` (e.g. `https://dev.appcloud.systems`)

3. **Test the login** - the redirect URI mismatch error should be resolved.

### Note on stable URLs

Because the app sits behind an ALB on a domain-derived host (not an API Gateway with a per-deployment ID), the redirect URI is stable per environment — you only register it once per branch/domain rather than after every deployment.

## Next Steps

Once Google OAuth is working:

1. Implement user registration in the database
2. Add role-based authorization
3. Extend to support additional OAuth providers
4. Configure production deployment with proper secrets management

## References

- [Google OAuth 2.0 Documentation](https://developers.google.com/identity/protocols/oauth2)
- [ASP.NET Core Google Authentication](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins)
- [AWS Secrets Manager](https://aws.amazon.com/secrets-manager/)