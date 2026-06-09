## 1. Prerequisite

- [x] 1.1 Confirm [`stabilize-ui-tests`](../stabilize-ui-tests/proposal.md) is merged (shared `OAuthTokenFixture` / `AppReadinessFixture` / `BaseUiTest` in place, and the two target tests are currently skipped with a pointer to this change).

## 2. Test-auth endpoint in Tjb.Web

- [x] 2.1 Add a Google id_token validator to `Tjb.Web` (e.g. `Google.Apis.Auth` `GoogleJsonWebSignature.ValidateAsync` with `Audience` = the configured Google client id; verify `iss`, `aud`, `exp`, signature).
- [x] 2.2 Add a gated test-auth endpoint (e.g. `POST /test-auth/signin`) registered ONLY when `TestAuth:Enabled` is true. It validates the posted id_token, resolves/provisions the Identity user using the same logic as the real external-login flow, and calls `SignInManager.SignInAsync` to issue the real cookie.
- [x] 2.3 Wire the `TestAuth:Enabled` config flag with a default of **false**.

## 3. Production safety gate

- [x] 3.1 In `infrastructure/`, pass `TestAuth:Enabled=true` only to per-feature-branch app stacks (`application.template`); ensure shared-infra stacks (`master.template` → `app`/`beta`/`alpha`) leave it false/unset.
- [x] 3.2 Add an automated check that the endpoint is unreachable/disabled when the gate is off (unit test for the registration gate, and/or a post-deploy probe that asserts 404 on a production-shaped config).

## 4. UI test wiring

- [x] 4.1 Extend `OAuthTokenFixture` to also capture and expose the Google **id_token** from the token response.
- [x] 4.2 Replace `LoginPage.AuthenticateWithTokenAsync` (raw-cookie injection) with a call to the test-auth endpoint that posts the id_token and lets the response set the real Identity cookie on the browser context.
- [x] 4.3 Un-skip `GoogleOAuthTests.TokenBasedGoogleLogin_ShouldAuthenticateWithValidToken` and `UserAuthenticationTests.LoggedInUser_ShouldDisplayUserName`; rewrite them to assert authenticated nav (`Hello {user}`, `Manage`) via `Expect(...)` against the real session.

## 5. Validate and archive

- [x] 5.1 Run `openspec validate ui-test-authenticated-session --strict`. Must pass.
- [x] 5.2 Deploy a feature env with the gate on; confirm the two re-enabled tests pass against a real session, and confirm the endpoint is 404 on a gate-off (production-shaped) deploy.
- [x] 5.3 Update [CLAUDE.md](../../../CLAUDE.md): document the gated test-auth endpoint, its production-safety gate, and that UI auth-tests sign in through it.
- [x] 5.4 Archive via `/opsx:archive ui-test-authenticated-session`. Capability promoted to `openspec/specs/ui-test-authenticated-session/`.
