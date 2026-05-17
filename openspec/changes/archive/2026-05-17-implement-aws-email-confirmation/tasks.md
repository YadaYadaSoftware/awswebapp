## 1. Setup & Dependencies

- [x] 1.1 Add AWSSDK.SimpleEmail NuGet package to Tjb.Web project
- [x] 1.2 Verify AWS SDK dependencies are compatible with .NET 10.0 target framework
- [x] 1.3 Add any required view rendering NuGet packages if not available in-box

## 2. Create Email Service Abstraction

- [x] 2.1 Create `Services/IEmailService.cs` interface with `SendEmailAsync()` method
- [x] 2.2 Create `SendEmailRequest.cs` DTO with `To`, `Subject`, `HtmlBody`, `TextBody` properties
- [x] 2.3 Create `Services/AwsSesEmailService.cs` implementing IEmailService
- [x] 2.4 Implement AWS SES client initialization with region and credentials from configuration
- [x] 2.5 Implement `SendEmailAsync()` to call SES SDK and send email with both HTML and text
- [x] 2.6 Add error handling and logging in AwsSesEmailService

## 3. Create View Rendering Service

- [x] 3.1 Create `Services/IViewRenderService.cs` interface with `RenderToStringAsync()` method
- [x] 3.2 Create `Services/ViewRenderService.cs` implementing IViewRenderService
- [x] 3.3 Implement view rendering using IViewEngine and ActionContext
- [x] 3.4 Add proper error handling for missing views

## 4. Register Services in Dependency Injection

- [x] 4.1 Update `Program.cs` to add `IEmailService` registration as `AwsSesEmailService`
- [x] 4.2 Update `Program.cs` to add `IViewRenderService` registration as `ViewRenderService`
- [x] 4.3 Ensure AWS SDK configuration is loaded from appsettings/environment
- [ ] 4.4 Verify services are properly injected in test

## 5. Create Email Templates

- [x] 5.1 Create `Pages/EmailTemplates/` folder
- [x] 5.2 Create `Pages/EmailTemplates/ConfirmationEmail.cshtml` with model for email and token
- [x] 5.3 Design email template with application branding and professional styling
- [x] 5.4 Include confirmation button with clickable link: `/Account/ConfirmEmail?email={email}&token={token}`
- [x] 5.5 Test email template rendering with sample data

## 6. Integrate Email Sending in OAuth Flow

- [x] 6.1 Locate external login callback in `Pages/Account/` or authentication handler
- [x] 6.2 Inject `IEmailService` into ExternalLogin controller/handler
- [x] 6.3 After successful Google OAuth user creation, call email service to send confirmation
- [x] 6.4 Pass user email and confirmation token to email service
- [x] 6.5 Handle email sending exceptions gracefully (log but don't block registration)
- [x] 6.6 Redirect to confirmation page after email sent

## 7. Update Confirmation UI

- [x] 7.1 Update confirmation page to show "Check your email at {email} to verify your account"
- [x] 7.2 Remove placeholder text "This app does not currently have a real email sender registered"
- [x] 7.3 Add success message after email confirmation: "Your account has been verified"
- [x] 7.4 Add error handling for invalid/expired tokens with option to resend
- [x] 7.5 Ensure confirmation page uses ASP.NET Identity's `ConfirmEmailAsync()` to mark user verified

## 8. Environment Configuration

- [x] 8.1 Add `AWS_REGION` to appsettings.json (or keep as environment variable)
- [x] 8.2 Add `AWS_SES_SENDER_EMAIL` to appsettings.json (e.g., "noreply@appcloud.systems")
- [x] 8.3 Add AWS credentials configuration (use DefaultAWSCredentials provider via IAM role)
- [x] 8.4 Document environment variables needed for different deployments (dev, alpha, beta, app)
- [x] 8.5 Update local development appsettings.json with dummy/test values

## 9. Testing & Validation

- [x] 9.1 Build project and verify no compilation errors
- [x] 9.2 Run application locally and test Google OAuth flow
- [x] 9.3 Verify email is sent (check CloudWatch logs or use SES sandbox)
- [x] 9.4 Test confirmation link and verify token is validated correctly
- [x] 9.5 Verify user is marked as email-confirmed after clicking link
- [ ] 9.6 Test expired token scenario and resend confirmation flow
- [ ] 9.7 Test with multiple users to ensure no email conflicts

## 10. Deployment & Infrastructure

- [x] 10.1 Verify AWS SES sender identity is verified in us-east-1 region
- [x] 10.2 Verify AWS SES sender identity is verified in us-west-2 region
- [x] 10.3 Ensure CloudFormation template includes IAM permissions for SES SendEmail
- [x] 10.4 Set AWS_REGION environment variable in deployment for each region
- [x] 10.5 Set AWS_SES_SENDER_EMAIL environment variable in deployment
- [x] 10.6 Document SES configuration and verification steps in runbook

## 11. Documentation & Cleanup

- [x] 11.1 Update CLAUDE.md or project documentation with email configuration details
- [x] 11.2 Remove any temporary test email services or stubs
- [x] 11.3 Verify no console.log or debug code left in email service
- [x] 11.4 Update CI/CD pipeline if needed for email testing

## 12. Handle Existing Users in OAuth Callback

- [x] 12.1 In `ExternalLogin.OnGetCallbackAsync`, after sign-in attempt fails, look up the user by Google-provided email with `FindByEmailAsync`
- [x] 12.2 If user exists and Google login is not yet linked, call `UserManager.AddLoginAsync` to attach the external login to the existing account
- [x] 12.3 If existing user's `EmailConfirmed` is false, resend the confirmation email and redirect to `RegisterConfirmation`
- [x] 12.4 If existing user's `EmailConfirmed` is true, call `SignInManager.SignInAsync` and redirect to the return URL
- [x] 12.5 Add logging at each branch (linked existing, resent confirmation, signed in existing) for debuggability
- [ ] 12.6 Test the four scenarios: new user, existing+linked+confirmed, existing+linked+unconfirmed, existing+not-linked
