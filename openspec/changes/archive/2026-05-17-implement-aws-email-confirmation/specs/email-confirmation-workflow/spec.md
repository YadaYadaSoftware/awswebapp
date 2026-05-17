## ADDED Requirements

### Requirement: Send confirmation email after Google OAuth registration
The system SHALL automatically send a confirmation email when a user authenticates via Google OAuth for the first time.

#### Scenario: New user completes Google OAuth
- **WHEN** a user authenticates via Google OAuth and a new account is created
- **THEN** a confirmation email is sent to the user's email address from Google

#### Scenario: Confirmation email contains verification link
- **WHEN** a confirmation email is sent
- **THEN** the email contains a clickable link with format `/Account/ConfirmEmail?email={email}&token={token}`

#### Scenario: Confirmation email is sent before page redirect
- **WHEN** user completes Google OAuth signup
- **THEN** the confirmation email is sent and user is shown confirmation page without waiting

### Requirement: Auto-register user with Google-provided email
The system SHALL automatically create the user account using the email address provided by Google OAuth, without requiring the user to enter or confirm their email address.

#### Scenario: User completes Google OAuth for the first time
- **WHEN** a new user authenticates via Google OAuth
- **THEN** the system automatically creates an account using the email claim from Google's OAuth token without displaying an email entry form

#### Scenario: User is never prompted to enter email after Google sign-in
- **WHEN** a user signs in via Google OAuth
- **THEN** the user is NOT shown a form asking them to enter or confirm their email address (since Google has already provided it)

#### Scenario: User is redirected directly to "check your email" page
- **WHEN** Google OAuth completes and the account is auto-created
- **THEN** the user is redirected immediately to the confirmation instruction page (no Register button click required)

### Requirement: Auto-link Google OAuth to existing accounts by email
The system SHALL automatically link a Google OAuth login to an existing user account that matches the Google-provided email address, rather than failing with a duplicate-user error or silently returning to the login page.

#### Scenario: Existing user signs in with Google for the first time (no external login linked)
- **WHEN** a user whose `AspNetUsers` row already exists (matching the email Google returned) signs in via Google OAuth, and no Google entry exists in `AspNetUserLogins` for that user
- **THEN** the system adds the Google external login to that existing user (no new user is created, no duplicate-user error is shown), then proceeds based on the user's email-confirmation state

#### Scenario: Existing user has Google linked but email not confirmed
- **WHEN** a user with a linked Google login signs in via Google OAuth, and the user's `EmailConfirmed` flag is false
- **THEN** the system re-sends the confirmation email and redirects the user to the "check your email" page (instead of silently failing because `RequireConfirmedAccount` blocks sign-in)

#### Scenario: Existing user has Google linked and email confirmed
- **WHEN** a user with a linked Google login signs in via Google OAuth, and the user's `EmailConfirmed` flag is true
- **THEN** the system signs them in and redirects to the return URL

#### Scenario: Existing user becomes confirmed after first link-and-click
- **WHEN** a user whose account predated Google linking links Google via OAuth and then clicks the confirmation link
- **THEN** the confirmation link marks `EmailConfirmed = true` and the user can subsequently sign in via Google without re-confirmation

### Requirement: Display confirmation instructions to user
The system SHALL show users a message instructing them to check their email for confirmation.

#### Scenario: User sees confirmation instruction
- **WHEN** user completes Google OAuth registration
- **THEN** user is shown a page stating "Check your email to verify your account" with their email address displayed

#### Scenario: User sees confirmation page before email confirmed
- **WHEN** user is not yet email-confirmed
- **THEN** the UI explicitly shows the "check email" message instead of technical placeholder text

### Requirement: Confirm email and mark account verified
The system SHALL verify the user's email address when they click the confirmation link.

#### Scenario: User clicks confirmation link
- **WHEN** user clicks the email confirmation link with valid token
- **THEN** the user's email is marked as confirmed and account is verified

#### Scenario: Confirmation link is invalid
- **WHEN** user clicks a confirmation link with invalid or expired token
- **THEN** user sees an error message and is offered option to resend confirmation

#### Scenario: User confirms email successfully
- **WHEN** email is confirmed via valid link
- **THEN** user is redirected to dashboard/home page with success message "Your account has been verified"

### Requirement: Single email address per confirmation
The system SHALL send confirmation email to the user's registered email address only once per registration.

#### Scenario: No duplicate confirmation emails
- **WHEN** a user completes Google OAuth registration
- **THEN** exactly one confirmation email is sent (no duplicates)

### Requirement: Token expiration for email confirmation
The system SHALL use time-limited tokens for email confirmation.

#### Scenario: Token expires after 24 hours
- **WHEN** user receives confirmation email with token
- **THEN** the token is valid for 24 hours and expires after that period

#### Scenario: Expired token shows appropriate error
- **WHEN** user attempts to use an expired confirmation token
- **THEN** the system displays "Token has expired" error and offers to send new confirmation email

### Requirement: Google OAuth registration flow
The system SHALL handle first-time Google OAuth registration by automatically creating the user account, sending a confirmation email, and displaying a "check your email" message.

#### Scenario: User completes OAuth and receives confirmation email
- **WHEN** a new user signs in via Google OAuth
- **THEN** the system creates user account, sends confirmation email, and shows confirmation page

#### Scenario: User sees professional confirmation message
- **WHEN** user completes Google OAuth
- **THEN** the confirmation page shows "Check your email at {email}" instead of technical placeholder

### Requirement: Account verification status
The system SHALL require users to confirm their email address via the confirmation link before the account is marked verified.

#### Scenario: Unverified account cannot access protected features
- **WHEN** user attempts to access account before confirming email
- **THEN** system may redirect to confirmation page or show verification requirement (implementation dependent)

#### Scenario: Verified account has full access
- **WHEN** user confirms email via confirmation link
- **THEN** account is marked verified and user has access to all features
