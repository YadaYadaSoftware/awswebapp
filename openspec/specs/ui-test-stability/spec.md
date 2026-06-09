# ui-test-stability Specification

## Purpose
TBD - created by archiving change stabilize-ui-tests. Update Purpose after archive.
## Requirements
### Requirement: OAuth access tokens are refreshed at suite start and on staleness

The UI test suite SHALL refresh the Google OAuth access token at suite startup using the long-lived refresh token from `GOOGLE_TEST_REFRESH_TOKEN`. Tests SHALL read the access token from a shared fixture rather than reading `GOOGLE_TEST_ACCESS_TOKEN` directly from environment variables.

A test that runs more than 50 minutes after the most recent refresh SHALL trigger a re-refresh BEFORE the test executes its assertions. If the refresh call fails (network error, invalid refresh token, Google API error), the suite SHALL abort immediately with an "auth bootstrap failed" message identifying the upstream error — every subsequent test would fail with misleading auth-related errors otherwise.

#### Scenario: Suite starts and mints a fresh access token
- **WHEN** the UI test suite begins
- **THEN** the `OAuthTokenFixture` calls `https://oauth2.googleapis.com/token` with the refresh token, receives a new access token, and exposes it on the fixture's `AccessToken` property. No subsequent test reads `GOOGLE_TEST_ACCESS_TOKEN` directly

#### Scenario: Long-running suite refreshes mid-run
- **WHEN** a test executes more than 50 minutes after the most recent refresh
- **THEN** the fixture refreshes the access token before the test's assertions run; the test sees a fresh token

#### Scenario: Refresh failure aborts the suite
- **WHEN** the refresh call fails (e.g., the refresh token in `GOOGLE_TEST_REFRESH_TOKEN` is invalid or revoked)
- **THEN** the suite throws a clear `AuthBootstrapException` from the fixture's `InitializeAsync`; tests do not run; the workflow summary shows the upstream Google error rather than a cascade of test failures

### Requirement: Tests wait for application readiness before assertions

The UI test suite SHALL verify the deployed application is responsive before any test assertion runs. Implementation SHALL poll `${TEST_BASE_URL}/health` (or the equivalent project-specific health endpoint) every 2 seconds, accepting a 200 response as ready. The total wait budget SHALL be 5 minutes; if exceeded, the suite SHALL fail with an "app not ready" message identifying the URL polled and the last HTTP status seen.

Readiness check runs ONCE per suite. Individual tests SHALL NOT re-poll readiness (would waste time on every test).

#### Scenario: Warm app responds immediately
- **WHEN** the deploy is already warm and `/health` returns 200 on the first poll
- **THEN** the `AppReadinessFixture` completes within 2 seconds and tests proceed

#### Scenario: Cold deploy warms up within budget
- **WHEN** the deploy is cold and `/health` returns 503 for the first ~30 seconds, then 200
- **THEN** the fixture polls until it sees the 200, then completes. Tests proceed

#### Scenario: Deploy never becomes ready
- **WHEN** `/health` does not return 200 within 5 minutes
- **THEN** the fixture throws `AppNotReadyException` with the URL, last status, and elapsed time. The suite aborts (no test runs); the workflow summary shows the failure cleanly

### Requirement: All tests inherit from BaseUiTest with sensible Playwright defaults

Every UI test class in `src/Tjb.UiTests/` SHALL inherit from `BaseUiTest`. `BaseUiTest` SHALL configure:

- `Page.SetDefaultTimeout(30_000)` (30 seconds — covers SSR latency + ALB request time)
- `Page.SetDefaultNavigationTimeout(60_000)` (60 seconds for first navigation; supports cold-context)
- Apply the shared OAuth access token (from `OAuthTokenFixture`) as a cookie / Authorization header so authenticated pages don't redirect to sign-in

This SHALL ensure no test class re-derives these settings independently, preventing drift.

#### Scenario: New test class works without timeout boilerplate
- **WHEN** a new test class is added that inherits `BaseUiTest`
- **THEN** the `Page` instance the test receives already has the default timeouts set; the test does not need to call `SetDefaultTimeout` itself

#### Scenario: Test runs in a context with auth cookie applied
- **WHEN** a test navigates to an authenticated page (e.g., `/account/profile`)
- **THEN** the page renders the authenticated view without redirecting to `/Identity/Account/Login` (because the OAuth token is in the request)

### Requirement: Assertions use Playwright's `Expect(...)` pattern with explicit timeouts

UI test assertions about page state (title, element visibility, text content, URL) SHALL use Playwright's `Expect(...).ToBe*Async(...)` family rather than synchronous calls or `WaitForSelector` with arbitrary sleeps. Each `Expect` call SHALL include an explicit timeout no shorter than 10 seconds for content assertions, no shorter than 30 seconds for full-page-load assertions.

Tests SHALL NOT use:
- `Thread.Sleep` / `Task.Delay` as a wait mechanism (use `Expect(...)` with timeout instead).
- Synchronous Playwright calls in assertion context (e.g., `Assert.Equal(expected, page.TitleAsync().Result)`).
- `WaitForSelector` without an explicit timeout argument (default is too short for SSR rendering under load).

#### Scenario: Title assertion uses Expect
- **WHEN** a test asserts the page title
- **THEN** it uses `await Expect(page).ToHaveTitleAsync(expected, new() { Timeout = 10_000 });` and does NOT use `Assert.Equal` against a synchronous title read

#### Scenario: Element visibility uses Expect
- **WHEN** a test asserts a button is visible
- **THEN** it uses `await Expect(locator).ToBeVisibleAsync(new() { Timeout = 10_000 });` and does NOT use `Thread.Sleep` followed by `Assert.True(locator.IsVisibleAsync().Result)`

### Requirement: Tests marked flaky retry up to 2 times before reporting failure

Tests with observed intermittent failures after the above hardening SHALL be marked with `[RetryFact(MaxRetries = 2)]` (from the `xRetry` package or equivalent). A trace is always available because `BaseTest` starts tracing unconditionally for every test (see the `test-result-reporting` capability — `BaseTest.InitializeAsync` calls `Context.Tracing.StartAsync`, equivalent to `trace: 'always'`, not `on-first-retry`); the retry then runs from a clean context. Tests that pass on retry are still reported as passed (no false-red), but the trace artifact remains so the operator can investigate whether the flake reflects a product or test issue.

Blanket retry application to ALL tests is forbidden — that masks genuine regressions. Only tests with demonstrated intermittent failure get the attribute.

#### Scenario: Flaky test passes on second attempt
- **WHEN** a test marked `[RetryFact(MaxRetries = 2)]` fails its first attempt
- **THEN** xUnit retries the test (up to 2 times); if any retry passes, the test reports `Passed`. The first-attempt failure produces a Playwright trace artifact regardless

#### Scenario: Test fails all attempts
- **WHEN** a test marked `[RetryFact(MaxRetries = 2)]` fails its first attempt AND both retries
- **THEN** xUnit reports the test as `Failed`; the workflow turns red; the failure summary cites the last-attempt error message

#### Scenario: Unmarked test does not retry
- **WHEN** a test without the `RetryFact` attribute fails
- **THEN** xUnit reports `Failed` immediately, no retries. Confirms retry is opt-in

