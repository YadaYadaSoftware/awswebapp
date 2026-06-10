# ui-test-authenticated-session Specification

## Purpose
TBD - created by archiving change ui-test-authenticated-session. Update Purpose after archive.
## Requirements
### Requirement: Test-auth endpoint establishes a real Identity session from a verified Google id_token

`Tjb.Web` SHALL expose a test-auth sign-in endpoint that accepts a Google `id_token`, validates it, and — on success — issues a genuine ASP.NET Identity session cookie (`.AspNetCore.Identity.Application`) for the corresponding user via `SignInManager`. Validation SHALL require: a valid signature against Google's published keys, `iss` equal to `accounts.google.com` (or `https://accounts.google.com`), `aud` equal to the Google OAuth client id the app is configured with, and an unexpired `exp`. If any check fails, the endpoint SHALL reject the request without signing anyone in.

#### Scenario: Valid id_token signs the test user in
- **WHEN** a request is made to the test-auth endpoint with a freshly-minted, valid Google id_token for the configured client and test user
- **THEN** the endpoint validates the token, resolves/provisions the Identity user, and returns a real `.AspNetCore.Identity.Application` cookie; a subsequent request carrying that cookie renders the authenticated view (e.g. the `Hello {user}` greeting and `Manage` link)

#### Scenario: Invalid or mis-audienced token is rejected
- **WHEN** the endpoint receives an id_token that is expired, has the wrong `aud`, the wrong `iss`, or a bad signature
- **THEN** the endpoint returns an error, signs no one in, and issues no Identity cookie

### Requirement: Test-auth endpoint is disabled by default and inert in production

The test-auth endpoint SHALL be disabled unless explicitly enabled by configuration (e.g. `TestAuth:Enabled = true`). It SHALL be enabled only on non-production, per-feature-branch app environments and SHALL NOT be enabled on the shared-infrastructure environments `app`, `beta`, or `alpha`. When disabled, the endpoint SHALL NOT be reachable (e.g. returns 404) and SHALL NOT be capable of issuing a session.

#### Scenario: Endpoint absent on production-shaped deploy
- **WHEN** `Tjb.Web` is deployed with the test-auth gate off (as on `app`/`beta`/`alpha`)
- **THEN** requests to the test-auth endpoint do not authenticate and the endpoint is not exposed (404 / disabled)

#### Scenario: Endpoint available on a feature-branch test env
- **WHEN** `Tjb.Web` is deployed to a feature-branch app stack with the test-auth gate on
- **THEN** the endpoint accepts a valid Google id_token and establishes a session, enabling the UI suite's authenticated-page tests

### Requirement: UI tests verify authenticated pages through a real session

The UI test suite SHALL obtain its authenticated session via the test-auth endpoint using the Google `id_token` from the shared `OAuthTokenFixture`, and SHALL NOT rely on injecting a raw access token as an Identity cookie. The previously-skipped authenticated-UI tests SHALL be re-enabled and SHALL assert the authenticated navigation against the real session.

#### Scenario: Authenticated-UI tests run against a genuine session
- **WHEN** the UI suite runs against a deployed env with the test-auth gate on
- **THEN** `TokenBasedGoogleLogin_ShouldAuthenticateWithValidToken` and `LoggedInUser_ShouldDisplayUserName` are no longer skipped, sign in via the test-auth endpoint, and pass by asserting `Hello {user}` / `Manage` are visible

#### Scenario: No raw-token cookie injection remains
- **WHEN** the UI test code is inspected
- **THEN** no test establishes "authentication" by writing a Google access token into the `.AspNetCore.Identity.Application` cookie; authentication goes through the validated test-auth endpoint

