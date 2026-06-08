## Why

Currently, when new users register via Google OAuth, the application displays a placeholder message indicating that no email sender is configured. This is a poor user experience and delays account verification. By integrating AWS SES for email delivery, we can send automated confirmation emails immediately after first-time Google authentication, completing the verification flow and reducing friction for new users.

## What Changes

- **Add AWS SES email service integration** - Wire up AWS SES (Simple Email Service) to send emails from the application
- **Implement email confirmation flow** - When a user authenticates via Google for the first time, send a confirmation email with a verification link
- **Create email confirmation template** - Design and implement an HTML email template for account confirmation
- **Replace placeholder UI** - Remove the "This app does not currently have a real email sender registered" message and show users "Check your email to verify your account"
- **Add email verification endpoint** - Create a controller endpoint to handle confirmation link clicks and mark accounts as verified
- **Update database schema** - Add fields to track email confirmation status if not already present
- **Configure AWS SES in environments** - Set up SES identity verification and environment variables for connection

## Capabilities

### New Capabilities
- `aws-ses-email-integration`: AWS SES service integration for sending transactional emails from the application
- `email-confirmation-workflow`: Complete workflow for sending and verifying account confirmation emails after Google OAuth signup
- `email-template-system`: Email template system for rendering and sending HTML emails with dynamic content

### Modified Capabilities
- `google-oauth-registration`: The registration flow changes to send a confirmation email instead of showing a placeholder message
- `account-verification`: Moves from a UI placeholder to an actual email-based verification process with database status tracking

## Impact

- **Affected code**: `Tjb.Web` Blazor components (registration/confirmation pages), `Program.cs` (service configuration), `TjbDbContext` (user confirmation fields if needed)
- **New dependencies**: AWS SDK for .NET (AWSSDK.SimpleEmail), email template library (potentially)
- **Environment variables**: AWS SES configuration (region, sender email, IAM credentials)
- **Database**: May require migration if email confirmation status fields are missing
- **Infrastructure**: AWS SES sender identity must be verified in target AWS account/region
- **Security**: New email sending credentials must be managed via AWS Secrets Manager or environment variables
