## Why

The post-deployment UI tests in [src/Tjb.UiTests/](../../../src/Tjb.UiTests/) (Playwright + xUnit, running against a deployed env URL) fail intermittently with no clear pattern. Four root causes have been identified:

1. **OAuth token expiry mid-suite.** Tests authenticate using `GOOGLE_TEST_ACCESS_TOKEN` / `GOOGLE_TEST_REFRESH_TOKEN` from CI secrets. Access tokens are short-lived (~1 hour). When the suite takes >1 hour, or when the same secret is used across multiple runs in quick succession, tests silently start hitting the OAuth wall and every auth-gated assertion fails.
2. **Cold-start / ALB warmup.** The first HTTP request to a newly-deployed Web container takes ~30-60 seconds while the ECS task starts and the ALB target health-checks it. If a test hits the app inside that window, it times out and cascades — subsequent tests fail because the test harness hasn't waited for app readiness.
3. **Element-not-ready races.** Many tests assert visibility / click elements immediately after navigation rather than awaiting them. Playwright handles some of this automatically, but assertion patterns like `Assert.Equal(..., page.Title())` (synchronous) or short-timeout `WaitForSelector` calls fail when the page is still loading.
4. **Genuine flake.** A subset of tests fail randomly with no pattern even after #1-#3 are addressed. Per-test retries are the realistic mitigation.

This change hardens against all four. It does NOT add visibility — that's the sibling change [`test-summary-reporting`](../test-summary-reporting/proposal.md), which should land first so the impact of these hardening changes is measurable.

## What Changes

- **ADDED capability `ui-test-stability`** covering each of the four classes of failure.
- **OAuth token refresh check at suite start:**
  - Add a fixture / `[AssemblyInitialize]` hook that runs before any test. Calls the Google OAuth token endpoint with the refresh token to mint a fresh access token. Replaces the in-memory access token used by every subsequent test.
  - Token refresh is also called proactively if any test class detects a token-age > 50 minutes (defensive — covers long suites).
  - If refresh fails, the suite aborts with a clear "auth bootstrap failed" message rather than letting every test fail with a misleading "expected sign-in, got 401" assertion.
- **ALB / cold-start warmup:**
  - Add a `WaitForAppReady` step at the top of the test suite that polls `https://<branch-leaf>.{domain}/health` (or equivalent) every 2 seconds with a 5-minute total budget. Tests do not start until the endpoint returns 200.
  - Each test class's setup also performs a single warmup GET to its target page so per-page lazy-init doesn't surface as test-side timeout.
- **Element-ready patterns:**
  - Audit existing tests for synchronous title/text/visibility assertions. Replace with `await Expect(locator).ToBeVisibleAsync(new() { Timeout = 10_000 })`.
  - Replace `WaitForSelector` calls with shorter explicit timeouts with `Expect(...)` assertions that have built-in retry semantics.
  - Add a `BaseUiTest` class that provides a `Page` with sensible defaults: `defaultTimeout: 30_000`, `defaultNavigationTimeout: 60_000` (longer for SSR + ALB latency).
- **Per-test retries:**
  - Configure xUnit to retry failed tests up to 2 times before reporting failure. Use the `xRetry` or `Xunit.RetryFact` package, or per-test `[Retry(2)]` attribute.
  - First-attempt failures still capture Playwright traces (per `test-summary-reporting`'s `trace: 'on-first-retry'` setting), so flake-vs-real-failure is debuggable.
- **No new test infrastructure** — keep xUnit + Playwright + the existing token-based auth. The changes are all additive: setup hooks, base class, retries, assertion patterns.

## Capabilities

### New Capabilities

- `ui-test-stability`: the four hardening contracts — OAuth refresh, ALB warmup, element-ready waits, per-test retries — that together cover the observed intermittent-failure modes.

### Modified Capabilities

(none)

## Impact

**Files touched:**
- `src/Tjb.UiTests/BaseTest.cs` (or create if absent) — central base class with the new patterns.
- `src/Tjb.UiTests/Fixtures/` — new `OAuthTokenFixture` (handles refresh) and `AppReadinessFixture` (handles warmup). Or assembly-level fixture if xUnit's collection-fixture pattern is in use.
- Possibly `src/Tjb.UiTests/Tjb.UiTests.csproj` — add `xRetry` package dependency.
- Existing test files (estimated 5-10 files) — replace problematic assertion patterns with element-ready waits. Mostly mechanical.
- `src/Tjb.UiTests/playwright.config.ts` (or .NET equivalent) — bump default timeouts.

**External dependencies added:**
- `xRetry` (NuGet, ~6k weekly downloads, MIT licensed) OR `Xunit.RetryFact` (smaller, similar shape). Either is fine; pick during implementation.

**Doesn't change:**
- The Playwright tests' assertions themselves (what they verify). Only HOW they wait and recover.
- CI workflow shape. Same `dotnet test` invocation; the test project now retries internally.
- Token-based auth model. Still uses the same Google secrets; just refreshes them defensively.

**Depends on `test-summary-reporting`** for measuring success: without the rich summary + trace artifacts, "is this less flaky now?" requires log-spelunking. Land `test-summary-reporting` first.

**Out of scope (deferred):**
- Multi-browser test coverage (currently single browser; Firefox/WebKit are separate work).
- Visual regression testing.
- Test parallelization tuning (current suite is small enough that single-threaded is fine).
- Mocking external dependencies. Google OAuth is real; that's intentional (we want end-to-end confidence including the OAuth callback path).
- Headed-mode local debugging tooling.
