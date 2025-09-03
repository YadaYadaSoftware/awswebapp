# Google OAuth Setup Guide

## Overview
This guide explains how to set up Google OAuth authentication for the TaskManager application.

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

## Step 3.5: Automated OAuth Configuration

The GitHub Actions workflow now includes automated OAuth configuration that generates the exact values needed for production deployments.

**Required GitHub Secrets for Automation:**
Add these to your repository secrets (Settings → Secrets and variables → Actions):

- `GOOGLE_OAUTH_CLIENT_ID` - Your Google OAuth 2.0 Client ID
- `GOOGLE_CLOUD_SERVICE_ACCOUNT_KEY` - Service account key JSON (for future API automation)
- `GOOGLE_CLOUD_PROJECT_ID` - Your Google Cloud project ID
- `GOOGLE_CLOUD_SERVICE_ACCOUNT_EMAIL` - Service account email

**Automated Process:**
1. After successful deployment, the workflow extracts your API Gateway ID
2. Generates the correct redirect URI and JavaScript origin URLs
3. Provides clear instructions for updating Google Cloud Console
4. Runs automatically on every main branch deployment

## Step 3.6: Manual Production Redirect URIs

**If you prefer manual updates, here's how to update the Google Cloud Console with the actual API Gateway URL:**

1. **Get your API Gateway URL** from the CloudFormation outputs:
   ```bash
   aws cloudformation describe-stacks \
     --stack-name taskmanager-main \
     --query 'Stacks[0].Outputs[?OutputKey==`ApiEndpoint`].OutputValue' \
     --output text
   ```

2. **Update Google Cloud Console**:
   - Go to **APIs & Services** > **Credentials**
   - Edit your OAuth 2.0 Client ID
   - Add to **Authorized redirect URIs**:
     - `https://[YOUR-API-GATEWAY-ID].execute-api.us-east-1.amazonaws.com/Prod/signin-google`
   - Add to **Authorized JavaScript origins**:
     - `https://[YOUR-API-GATEWAY-ID].execute-api.us-east-1.amazonaws.com`

3. **Alternative: Use Custom Domain** (Recommended for production):
   - Configure a custom domain for your API Gateway
   - Update Google Console with the custom domain
   - This provides consistent URLs across deployments

## Step 4: Configure Application Settings

### For Local Development

#### Option 1: User Secrets (Recommended)
```bash
# Navigate to the API project
cd src/TaskManager.Api

# Set the Google OAuth credentials
dotnet user-secrets set "Authentication:Google:ClientId" "your-google-client-id-here"
dotnet user-secrets set "Authentication:Google:ClientSecret" "your-google-client-secret-here"

# Navigate to the Web project
cd ../TaskManager.Web

# Set the same credentials for the Web project
dotnet user-secrets set "Authentication:Google:ClientId" "your-google-client-id-here"
dotnet user-secrets set "Authentication:Google:ClientSecret" "your-google-client-secret-here"
```

**Note**: The projects have been configured with `UserSecretsId` properties to enable user secrets functionality.

#### Option 2: appsettings.Development.json (Less Secure)
Add to both `src/TaskManager.Api/appsettings.Development.json` and `src/TaskManager.Web/appsettings.Development.json`:

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
   # For API
   dotnet run --project src/TaskManager.Api

   # For Web (in another terminal)
   dotnet run --project src/TaskManager.Web
   ```

2. Navigate to the web application (usually `https://localhost:7001`)
3. Click the "Login with Google" button
4. Complete the Google OAuth flow
5. Verify you're redirected back and logged in

## API Endpoints

The API provides these authentication endpoints:

- `GET /api/auth/login` - Initiate Google OAuth login
- `GET /api/auth/login-callback` - Handle OAuth callback
- `GET /api/auth/user` - Get current user info (requires authentication)
- `POST /api/auth/logout` - Logout user
- `GET /api/auth/status` - Check authentication status

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
    - **For production deployments**: Update Google Console with the actual API Gateway URL after deployment
    - **Example**: `https://pkuatgoyed.execute-api.us-east-1.amazonaws.com/Prod/signin-google`

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

### Quick Fix for Current Deployment

1. **Get your current API Gateway URL**:
   ```bash
   aws cloudformation describe-stacks \
     --stack-name taskmanager-main \
     --query 'Stacks[0].Outputs[?OutputKey==`ApiEndpoint`].OutputValue' \
     --output text
   ```

2. **Update Google Cloud Console**:
   - Go to [Google Cloud Console](https://console.cloud.google.com/)
   - Navigate to **APIs & Services** > **Credentials**
   - Edit your OAuth 2.0 Client ID
   - Add these **Authorized redirect URIs**:
     - `https://[YOUR-API-GATEWAY-ID].execute-api.us-east-1.amazonaws.com/Prod/signin-google`
   - Add these **Authorized JavaScript origins**:
     - `https://[YOUR-API-GATEWAY-ID].execute-api.us-east-1.amazonaws.com`

3. **Replace `[YOUR-API-GATEWAY-ID]`** with the actual ID from step 1 (e.g., `pkuatgoyed`)

4. **Test the login** - the redirect URI mismatch error should be resolved

### For Future Deployments

Consider setting up a custom domain for your API Gateway to avoid this issue:
- Use Route 53 + API Gateway custom domain
- Update Google Console once with the custom domain
- No need to update Google Console after each deployment

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