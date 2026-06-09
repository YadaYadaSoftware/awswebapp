## Context

`Tjb.Web` is the deployed Blazor Server app using ASP.NET Identity + Google OAuth. The UI tests (`Tjb.UiTests`, Playwright + xUnit) run post-deploy against the live branch URL `https://<branch-leaf>.{domain}`. After [`stabilize-ui-tests`](../stabilize-ui-tests/proposal.md), a shared `OAuthTokenFixture` mints a fresh Google **access token** at suite start from `GOOGLE_TEST_REFRESH_TOKEN`.

The only way to authenticate to `Tjb.Web` today is the interactive Google OAuth redirect → `/signin-google` callback. The UI suite has no way to *complete* that flow non-interactively, so the token-based tests faked it by writing a cookie — which doesn't work (see proposal; confirmed byte-identical anonymous response). We need a real session.

Google's token endpoint returns both an `access_token` and an `id_token` (a signed JWT identifying the user) for the `openid` scope, which the suite already requests. The `id_token` is the natural credential for a server-side sign-in: it's verifiable (signature + issuer + audience + expiry) without a second network hop.

## Goals / Non-Goals

**Goals:**
- Let the UI suite obtain a genuine `.AspNetCore.Identity.Application` session cookie for the test user, so authenticated-UI assertions are real.
- Validate a real Google `id_token` server-side (signature against Google JWKS, `iss = https://accounts.google.com`, `aud = configured Google client id`, not expired) before signing anyone in — no unauthenticated bypass.
- Be **provably inert in production**: disabled by default, enabled only on non-production test environments via an explicit gate; off on `app`/`beta`/`alpha`.
- Re-enable and rewrite the two skipped tests to assert authenticated nav against the real session.

**Non-Goals:**
- Multi-provider test auth (only Google).
- Arbitrary role/claim impersonation or "log in as any user" — only the holder of a valid Google id_token for the configured client.
- Replacing the real interactive OAuth path (it stays; this is test-only).
- Driving the Google consent UI in-browser (rejected below).

## Decisions

### D1. Test-auth endpoint in `Tjb.Web` that exchanges a verified Google id_token for a real sign-in (preferred)

Add a minimal endpoint (e.g. `POST /test-auth/signin`) that:
1. Is **gated** — returns 404/disabled unless `TestAuth:Enabled` config is true. Registered only when the gate is on.
2. Accepts a Google `id_token` (header or form field).
3. Validates it: signature via Google's published JWKS, `iss ∈ {accounts.google.com, https://accounts.google.com}`, `aud == GoogleOAuth client id` the app is configured with, `exp` in the future. Prefer `Google.Apis.Auth`'s `GoogleJsonWebSignature.ValidateAsync` with `Audience` set.
4. Resolves the Identity user by the verified email (provision if the app's normal external-login flow would have), then `await SignInManager.SignInAsync(user, isPersistent: false)` — issuing the genuine Identity cookie.

**Why id_token, not access_token:** the id_token is a signed assertion of identity the app can verify offline; the access token is an opaque bearer for Google APIs and carries no verifiable identity for *our* app. This is exactly why the cookie-injection hack failed.

**Why an endpoint vs. reusing `/signin-google`:** the real callback expects Google's `code`/`state` correlation and a browser redirect dance that can't be replayed non-interactively. A dedicated, validated, gated endpoint is the smallest honest surface.

### D2. Gate: disabled by default, on only for non-production test envs

`TestAuth:Enabled` defaults **false**. It is set true only for feature-branch app stacks via `infrastructure/` (the per-branch `application.template`), and explicitly **not** passed (or passed false) for the shared-infra `app`/`beta`/`alpha` stacks (`master.template`). Design will confirm the cleanest wiring (a parameter defaulting to false; only feature stacks override). A tasks step must *verify* the flag is off on a production-shaped deploy.

### D3. UI test wiring

- `OAuthTokenFixture` additionally exposes the **id_token** (already in the same Google token response — just capture it).
- Replace `LoginPage.AuthenticateWithTokenAsync` (cookie injection) with a call to `POST /test-auth/signin` carrying the id_token; the response sets the real Identity cookie on the browser context.
- Un-skip and rewrite the two tests to navigate to an authenticated page and assert `Hello {user}` / `Manage` are visible via `Expect(...)`.

### D4. Alternative considered — drive the real OAuth UI in-browser (rejected)

Playwright could script the Google consent screen. Rejected: brittle (Google actively breaks automation, adds challenges/2FA), slow, and re-introduces exactly the flake `stabilize-ui-tests` is removing. The gated endpoint keeps the OAuth *contract* honest (real signed token, real SignInManager) without the consent-UI fragility.

## Risks / Trade-offs

- **[Risk] A sign-in endpoint is a high-value target.** Mitigation: disabled by default; only enabled on throwaway feature envs; validates a real Google id_token (signature/iss/aud/exp) so it cannot mint a session without a genuine Google-issued token for *our* client; a tasks step asserts it is 404/off on a production-shaped deploy.
- **[Risk] Gate misconfiguration could ship it to production.** Mitigation: default-false parameter, explicit non-override on shared-infra templates, and an automated check (test or post-deploy probe) that the endpoint is absent on `app`/`beta`/`alpha`.
- **[Trade-off] Adds a Google token-validation dependency to `Tjb.Web`** (`Google.Apis.Auth`). Accepted: small, official, and the correct way to validate id_tokens.
- **[Trade-off] Test-auth path diverges slightly from the real `/signin-google` user-provisioning flow.** Mitigation: reuse the same user-resolution/provisioning logic the external-login handler uses, so a test session matches a real one.
