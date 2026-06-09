## Why

The UI tests cannot verify any *authenticated* page. The token-based tests call `LoginPage.AuthenticateWithTokenAsync()`, which injects a raw Google OAuth access token as the `.AspNetCore.Identity.Application` cookie — but that is not a valid data-protector-signed Identity auth ticket, so `Tjb.Web` correctly ignores it and serves the anonymous view. This was confirmed empirically: fetching the deployed app's home page with that exact cookie returns a **byte-identical** anonymous page (login link present, no `Hello {user}` greeting). The tests only ever *looked* green because they skipped whenever the test token was missing/expired; once fed a genuinely valid token, they fail deterministically.

As a result, two tests are being **skipped** in [`stabilize-ui-tests`](../stabilize-ui-tests/proposal.md) pending this work:
- `GoogleOAuthTests.TokenBasedGoogleLogin_ShouldAuthenticateWithValidToken`
- `UserAuthenticationTests.LoggedInUser_ShouldDisplayUserName`

This change gives the UI tests a way to reach a **real** authenticated session, so authenticated-UI coverage is genuine rather than a no-op. (The real OAuth *redirect* path is already covered and passing via `GoogleLogin_ShouldRedirectToGoogle`; what's missing is a *completed* sign-in.)

## What Changes

- **ADDED capability `ui-test-authenticated-session`** — a safe, environment-gated mechanism for the UI test suite to obtain a genuine ASP.NET Identity session against a deployed `Tjb.Web` instance.
- **A test-auth sign-in endpoint in `Tjb.Web`** (preferred approach, to be confirmed in design): accepts a Google **id_token**, validates its signature, issuer (`accounts.google.com`), audience (the configured Google client id), and expiry, resolves/provisions the corresponding Identity user, and issues a real Identity sign-in cookie via `SignInManager`. Returns the standard `.AspNetCore.Identity.Application` cookie the browser then carries.
- **Hard production safety**: the endpoint is **disabled by default** and only enabled on non-production, test-targeted environments (gated by configuration/branch). It must be inert on `app`/`beta`/`alpha`. It never bypasses Google verification — it validates a real, freshly-minted Google id_token; it is not a "log in as anyone" backdoor.
- **UI test changes**: replace the non-functional `AuthenticateWithTokenAsync` cookie injection with a call to the test-auth endpoint (using the id_token the `OAuthTokenFixture` already obtains alongside the access token), then **un-skip** the two tests so they assert the authenticated nav (`Hello {user}`, `Manage` link) against a real session.
- **Alternative considered (design will decide)**: drive the full Google OAuth callback in-browser instead of a test-auth endpoint. Heavier and flakier (real consent UI), but zero app-side test surface.

## Capabilities

### New Capabilities

- `ui-test-authenticated-session`: a production-safe, environment-gated path for the UI tests to establish a genuine ASP.NET Identity session (validated Google id_token → real sign-in cookie), plus the test-side wiring that uses it to verify authenticated pages.

### Modified Capabilities

(none — `ui-test-stability` from `stabilize-ui-tests` is unchanged; this is additive. The two skipped tests are re-enabled by this change but their stability contract doesn't change.)

## Impact

**Files likely touched:**
- `src/Tjb.Web/` — new test-auth sign-in endpoint (Razor Page or minimal endpoint), gated by config (e.g. `TestAuth:Enabled`), using `SignInManager<IdentityUser>` and Google id_token validation (`Google.Apis.Auth` or `Microsoft.IdentityModel.Tokens` + Google JWKS).
- `infrastructure/` — pass the gating flag only to non-production app stacks (off for `app`/`beta`/`alpha`).
- `src/Tjb.UiTests/` — `OAuthTokenFixture` to also expose the **id_token**; `LoginPage`/`BaseUiTest` to call the test-auth endpoint; un-skip the two tests and assert authenticated nav.

**Security surface:** introduces an authentication path that must be provably inert in production. Design must specify the gate, the token validation (issuer/audience/expiry/signature), and how the gate is verified to be off on shared-infra branches.

**Dependencies:** builds on `stabilize-ui-tests` (shared `OAuthTokenFixture` / `AppReadinessFixture` / `BaseUiTest`). Possibly adds a Google token-validation library to `Tjb.Web`.

**Out of scope:** multi-provider test auth, role/claims fixtures beyond a basic signed-in user, load/perf of the endpoint.
