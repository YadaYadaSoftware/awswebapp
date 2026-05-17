## Context

The application uses ASP.NET Identity for user management and Google OAuth for authentication. When a user authenticates via Google for the first time, the system creates a new user account but currently displays a placeholder message from ASP.NET Identity's email confirmation flow: "This app does not currently have a real email sender registered."

The user's email address is obtained from Google's OAuth token and automatically populated in the Identity user. We need to leverage this email to send a confirmation message that allows the user to verify their account, mirroring the standard ASP.NET Identity email confirmation pattern.

**Current State**: 
- Google OAuth creates user with populated email
- Email confirmation UI shows placeholder text
- No email is actually sent
- User cannot complete account verification

**Constraints**:
- Application already runs on AWS infrastructure (ALB, CloudFormation, Aurora, ECR)
- .NET 10.0 with ASP.NET Identity framework
- Multi-region deployment (us-east-1, us-west-2) with consideration for email service availability

## Goals / Non-Goals

**Goals:**
- Send automated confirmation email to newly registered Google OAuth users
- Provide a secure, clickable verification link that confirms email and marks user as verified
- Remove the email sender placeholder message and guide users to check their email
- Maintain compatibility with ASP.NET Identity's email confirmation infrastructure
- Support all deployment environments (dev, alpha, beta, app)

**Non-Goals:**
- Implement additional email features (newsletters, notifications, password reset emails)
- Change or enhance the Google OAuth flow itself
- Modify the Identity user creation logic
- Implement email retry/bounce handling
- Create a generic email templating system for other use cases

## Decisions

### 1. Email Service: AWS SES
**Decision**: Use AWS Simple Email Service (SES) for sending emails.

**Rationale**: 
- Application already deployed on AWS infrastructure
- SES is cost-effective for transactional email
- Tight integration with IAM for credentials management
- No additional third-party vendor needed

**Alternatives Considered**:
- SendGrid: Requires external account, adds dependency, but has better dashboard and retry logic
- SMTP server: Less reliable, requires separate infrastructure
- Custom mail service: Too much overhead for this scope

### 2. Email Service Integration Pattern
**Decision**: Create an `IEmailService` abstraction with a concrete `AwsSesEmailService` implementation.

**Rationale**:
- Decouples email sending from SES details
- Makes testing easier (can mock the interface)
- Allows future email provider changes without touching business logic
- Follows dependency injection pattern already used in the application

**Implementation**:
- `Services/IEmailService.cs` - Interface for sending emails
- `Services/AwsSesEmailService.cs` - AWS SES implementation
- Register in `Program.cs` dependency injection container
- Service accepts a `SendEmailRequest` DTO with `To`, `Subject`, `HtmlBody`, `TextBody`

### 3. Confirmation Token Generation
**Decision**: Use ASP.NET Identity's built-in `UserManager.GenerateEmailConfirmationTokenAsync()` and `UserManager.ConfirmEmailAsync()`.

**Rationale**:
- Leverages existing Identity infrastructure
- Token is cryptographically secure and time-bound
- No need to implement custom token generation
- Identity already knows how to validate tokens

**Token Format**: Identity token is embedded in confirmation URL: `/Account/ConfirmEmail?email={email}&token={token}`

### 4. Email Template Delivery
**Decision**: Render email templates as Razor views in a dedicated `EmailTemplates/` folder.

**Rationale**:
- Familiar to .NET developers
- Can reuse layout, helpers, and existing CSS/styling
- View engine is already running, no additional dependency
- Easy to preview/test

**Template Location**: `Pages/EmailTemplates/ConfirmationEmail.cshtml`

**Rendering**: Inject `IViewRenderService` (custom service) to render Razor views to HTML strings.

### 5. Confirmation Email Trigger Point
**Decision**: Send confirmation email immediately after successful Google OAuth external login sign-in (in the ExternalLoginCallback path).

**Rationale**:
- Occurs at the moment of first-time registration
- User session already established
- Can immediately redirect to confirmation page with clear messaging
- Aligns with standard authentication flow

**Implementation**: In `Tjb.Web/Pages/Account/ExternalLogin.cshtml.cs`, after user creation via Google OAuth, call `await emailService.SendConfirmationEmailAsync()` before returning to confirmation page.

### 5a. Auto-Registration with Google-Provided Email (Skip Email Entry Form)
**Decision**: Automatically create the user account using the email address from Google's OAuth claims; do NOT present an email entry/confirmation form.

**Rationale**:
- Google OAuth always provides a verified email address via the `email` claim
- Asking the user to re-enter an email they've already provided creates unnecessary friction
- The default ASP.NET Identity `ExternalLoginConfirmation` form is designed generically for OAuth providers that may not provide email — it's not needed for Google
- Reduces the flow from "Login → Form → Click Register → Confirmation page" to "Login → Confirmation page" (one fewer click)

**Implementation**:
- Override the default Identity `ExternalLogin` flow so that when `info.Principal.FindFirstValue(ClaimTypes.Email)` returns a value, the user is auto-created and redirected to the confirmation page without rendering the email entry form
- The `ExternalLoginConfirmation` page becomes a read-only "Check your email at {email}" page (no input field, no submit button)
- If Google somehow does not return an email claim (edge case), fall back to showing the email entry form

**Alternatives Considered**:
- Keep the default Identity form: simpler implementation, but creates the redundant "enter your email" step the user already completed via Google
- Skip the confirmation page entirely and sign user in immediately: would bypass email verification requirement (rejected — we need email verification)

### 5b. Auto-Link Google to Existing Accounts (by Email Match)
**Decision**: When Google OAuth returns an email that matches an existing `AspNetUsers` row, automatically attach the Google `AspNetUserLogins` entry to that existing user instead of attempting to create a duplicate user.

**Rationale**:
- Without this, the OAuth callback hits a `DuplicateUserName` error from `UserManager.CreateAsync()` and silently redirects to `/Login`, which looks like a dead-end "nothing happened" to the user
- Google verifies email ownership, so it's safe to trust the email match — anyone who can pass Google OAuth for `foo@gmail.com` has demonstrated control over that mailbox
- This also covers the case where an account was created out-of-band (seed data, migration from another provider, manual cleanup of `AspNetUserLogins` rows during testing)
- Also handles the related case where `ExternalLoginSignInAsync()` returns `IsNotAllowed` because `RequireConfirmedAccount = true` and the user has not yet confirmed — instead of falling through to the duplicate-user error, we resend the confirmation email and redirect to the "check your email" page

**Implementation** (in `ExternalLogin.OnGetCallbackAsync`):
1. Try `ExternalLoginSignInAsync()`. If `Succeeded` → redirect to return URL. If `IsLockedOut` → lockout page.
2. Read the email claim from the external principal.
3. `FindByEmailAsync(email)` → if a user exists:
   - If no Google entry in their `AspNetUserLogins`, call `AddLoginAsync()` to link
   - If `EmailConfirmed == false`, resend confirmation email and redirect to `RegisterConfirmation`
   - If `EmailConfirmed == true`, call `SignInManager.SignInAsync()` and redirect to return URL
4. Otherwise (no existing user) → existing auto-create path runs.

**Trust boundary**: We trust Google's email verification. If we ever add an OAuth provider that does NOT verify emails (Twitter historically didn't), this auto-linking must be disabled or guarded for that provider only.

**Alternatives Considered**:
- Show the user a "we found an existing account, link it?" confirmation form: more conservative but adds friction for a flow that's already safe given Google's email verification
- Always treat `FindByEmailAsync` hits as an error and force the user to log in with their original method: would leave users stuck if they no longer remember how they originally registered, and offers no security benefit when Google has already verified the email

### 6. Account Verification Persistence
**Decision**: Rely on ASP.NET Identity's `EmailConfirmed` field to track verification status.

**Rationale**:
- No database schema changes needed (field already exists in IdentityUser)
- ASP.NET Identity already has `ConfirmEmailAsync()` method
- Existing authorization attributes can use `[RequireEmailConfirmed]` if needed

**No Migration Required**: `EmailConfirmed` is already part of the default Identity schema.

### 7. Confirmation Page UX
**Decision**: Show two separate pages:
- **Before Email Sent**: "Confirmation email sent to {email}. Check your email and click the link to verify your account."
- **After Confirmation**: "Your account has been verified. Welcome!"

**Rationale**:
- Clear user communication
- Prevents resubmission/confusion
- Standard pattern in web applications

## Risks / Trade-offs

### Risk: SES Sending Limits
**Mitigation**: 
- SES has soft limits (typically 14 emails/second in production after sandbox graduation)
- For typical user signup flow, this is not a bottleneck
- Monitor CloudWatch metrics for SES throttling
- If needed, implement retry logic with exponential backoff

### Risk: Email Delivery Failures (bounces, spam filters)
**Mitigation**:
- Send from verified domain identity in SES (not sandbox mode)
- Use proper authentication (SPF, DKIM, DMARC)
- Template should include branding and clear sender info
- Don't log user as verified until they click the link (current behavior, maintained)
- Consider implementing bounce notifications via SNS in future

### Risk: Token Expiration / Lost Emails
**Mitigation**:
- ASP.NET Identity tokens default to 24-hour expiration (configurable)
- If token expires, user can initiate resend (not in this change, but easy to add)
- Show error page if token is invalid/expired with link to request new token

### Risk: Regional Email Sending (Multi-Region Deployment)
**Consideration**:
- Both us-east-1 and us-west-2 deployments need SES sender identity verified
- Configure SES in both regions independently
- Application reads SES region from environment variable
- If SES unavailable in one region, other region continues to work

### Risk: Cost Growth
**Mitigation**:
- SES is $0.10 per 1,000 emails sent
- For 10K new users/month, cost is ~$1
- Not a significant expense compared to infrastructure
- Can set up billing alerts in AWS

### Trade-off: Single Sender Email
**Decision**: Use a single "noreply@" address for all transactional emails.

**Rationale**:
- Simplifies configuration
- Users don't expect to reply to transactional emails
- Reduces complexity of email template/settings

**Alternative**: Could allow per-environment sender addresses, but adds complexity.

## Migration Plan

1. **Add NuGet dependency**: `AWSSDK.SimpleEmail` (already available, add to project file)
2. **Implement email service**:
   - Create `IEmailService` interface
   - Implement `AwsSesEmailService` with AWS SDK calls
   - Create `IViewRenderService` for rendering Razor views
   - Register services in `Program.cs`
3. **Create email template**: `EmailTemplates/ConfirmationEmail.cshtml`
4. **Update ExternalLogin flow**: Hook email sending into post-OAuth-registration flow
5. **Update confirmation pages**: Replace placeholder message with real confirmation UI
6. **Configure environment variables**: AWS region, sender email, SES access credentials (via Secrets Manager in production)
7. **Test in dev environment**: Verify SES access, template rendering, confirmation flow
8. **Deploy to alpha/beta/app**: Requires SES sender identity verification in each region

**Rollback**: If SES integration fails:
- Comment out email sending in ExternalLogin
- Revert to placeholder message temporarily
- No database changes required (easy to roll back)

## Open Questions

1. Should we send confirmation emails to existing users who authenticated via Google but haven't confirmed email yet?
2. Should we implement a "Resend Confirmation Email" feature, or is one attempt sufficient?
3. What should happen if SES fails to send—should we block user registration or fail silently?
4. Should unverified Google OAuth accounts be able to use the app with limited functionality, or require email confirmation before any access?
