## Context

The UI test suite lives in [src/Tjb.UiTests/](../../../src/Tjb.UiTests/) and runs in CI's `post-deployment-ui-tests` job. Tests use Playwright for browser automation and xUnit as the runner. Authentication uses token-based Google OAuth (a refresh token stored in CI secrets is used to mint access tokens — no UI-scripted OAuth flow, intentionally).

Observed failure modes (per the proposal and from CI run history):

1. **OAuth token expiry mid-suite.** Access tokens last ~1 hour; the suite occasionally runs longer.
2. **Cold-start / ALB warmup.** First request after deploy takes 30-60s.
3. **Element-not-ready races.** Synchronous assertions or short waits fail when the page is still loading.
4. **Genuine flake.** Random failures even after #1-#3 are addressed.

This change addresses each cause. It depends on [`test-summary-reporting`](../test-summary-reporting/proposal.md) for measuring the impact (without trace artifacts and structured summaries, "is this less flaky" is hard to answer). Sequence: report first, harden second.

## Goals / Non-Goals

**Goals:**
- Eliminate OAuth expiry as a failure cause (refresh defensively at suite start + at >50min token age).
- Eliminate cold-start as a failure cause (block tests until the app is responsive).
- Reduce element-ready races by adopting `Expect(...).ToBe*Async()` patterns with sensible timeouts.
- Mask remaining genuine flake with up-to-2 per-test retries — but capture trace on first attempt so the flake is debuggable.
- Provide a `BaseUiTest` class so future tests inherit the new defaults by default rather than re-deriving them.

**Non-Goals:**
- Eliminating all test failures (some failures should be product bugs and should fail loudly — that's the point).
- Multi-browser coverage (separate concern, separate change).
- Visual regression / screenshot diffing.
- Mocking the Google OAuth callback (intentional end-to-end coverage).
- Parallelization (the suite is small; sequential is fine).
- Replacing xUnit or Playwright. The existing stack is fine — only adding to it.

## Decisions

### D1. Suite-level OAuth refresh via xUnit collection fixture

xUnit's `[CollectionDefinition]` + `IAsyncLifetime` pattern allows a single fixture instance to be shared across all tests in a collection. Use an `OAuthTokenFixture` that:

- On `InitializeAsync`: reads `GOOGLE_TEST_REFRESH_TOKEN` from env, calls Google's `https://oauth2.googleapis.com/token` endpoint, mints a fresh access token, exposes it via a public property.
- Tests pull the access token from the fixture rather than reading `GOOGLE_TEST_ACCESS_TOKEN` directly.
- `DisposeAsync`: no-op (tokens are short-lived; no cleanup needed).

For suites long enough to risk a 50+ minute access token, add a `RefreshIfStale` method called by `BaseUiTest`'s `[Fact]` setup that refreshes when token age > 50min.

**Why this shape**: minimal disruption to existing tests. Tests that read auth go from `Environment.GetEnvironmentVariable("GOOGLE_TEST_ACCESS_TOKEN")` to `Fixture.AccessToken`. The refresh logic is in one place.

**Alternative considered**: use the access token directly without refresh. Rejected — observed flake at the 60-90 minute mark proves this is needed.

### D2. ALB warmup via polling /health before tests start

A second fixture (`AppReadinessFixture`) polls `${TEST_BASE_URL}/health` every 2 seconds with a 5-minute budget. Returns success when a 200 is seen. Fails the suite if 5 minutes elapse with no success.

This runs ONCE per suite. The cost is at most 5 minutes of polling for a stuck deploy (which is a real signal worth surfacing), and as little as 2 seconds for a warm deploy.

**Why /health and not the home page**: `/health` is cheap to serve (no DB, no auth), set up specifically for ALB target-group health checks, and matches what the workflow's own deploy-validation already relies on.

**Why fixture vs the workflow doing it**: the workflow already waits for the CFN stack to reach `CREATE_COMPLETE` / `UPDATE_COMPLETE`. That signals infra-up, not app-warmed-up. The fixture handles the application-layer readiness which CFN can't see.

### D3. BaseUiTest with sensible Playwright defaults

```csharp
public abstract class BaseUiTest : IClassFixture<OAuthTokenFixture>, IClassFixture<AppReadinessFixture>
{
    protected readonly OAuthTokenFixture Auth;
    protected readonly AppReadinessFixture AppReady;
    protected IPage Page { get; private set; }
    protected IBrowserContext Context { get; private set; }

    protected BaseUiTest(OAuthTokenFixture auth, AppReadinessFixture appReady)
    {
        Auth = auth;
        AppReady = appReady;
    }

    protected async Task SetUpPageAsync()
    {
        // ... create context with DefaultTimeout=30_000, DefaultNavigationTimeout=60_000
        // ... apply auth cookie from Auth.AccessToken
        Page.SetDefaultTimeout(30_000);
        Page.SetDefaultNavigationTimeout(60_000);
    }
}
```

Existing tests refactor to inherit from `BaseUiTest`. Refactor is mostly mechanical — change inheritance + use `Page` instead of self-created context.

### D4. Replace synchronous assertions with `await Expect(...).ToBe*Async()`

Audit pattern: any test that does `Assert.Equal(expected, page.TitleAsync().Result)` or `Assert.True(page.IsVisibleAsync(...))` is a candidate.

Replacement:
- `await Expect(page).ToHaveTitleAsync(expected, new() { Timeout = 10_000 });`
- `await Expect(locator).ToBeVisibleAsync(new() { Timeout = 10_000 });`

Playwright's `Expect` assertions have built-in retry / wait semantics. They're the canonical way to assert in Playwright .NET.

Estimated ~10-20 sites to change across the existing test suite. Mechanical but worth doing carefully (don't change what's being asserted, only how the wait is structured).

### D5. Per-test retries via xRetry (or Xunit.RetryFact)

Use `[RetryFact(MaxRetries = 2)]` (or the equivalent attribute) on tests that have shown flake. **Don't blanket-apply to every test** — that masks real regressions.

Retry attempts on tests that are demonstrably flaky AFTER addressing #1-#3. The list of tests to mark grows / shrinks as we see new behavior.

First-attempt failures STILL capture Playwright traces (via the trace setting from `test-summary-reporting`), so retries don't hide debuggable signal.

### D6. Suite ordering matters for cost

Setup cost: token refresh ~1s, app warmup 2s-5min. Both should happen ONCE per suite, not per test. xUnit collection fixtures give us this for free (fixture instance shared across all tests in the collection).

If the suite is small enough to fit in one collection, we use a single `[Collection("UiTests")]` attribute on all test classes. If it grows enough to need parallel collections, we accept that warmup runs per-collection.

## Risks / Trade-offs

- **[Risk] Per-test retries hide regressions.** A test that genuinely broke (product bug) now retries 2x and might still pass once, then we miss the regression. → Mitigation: trace is captured on first-attempt failure even when retry passes. Periodic review of "tests that retry-passed" in summary catches drift.
- **[Risk] Increased suite runtime.** Warmup adds up to 5 min for cold deploys. Retries add ~2x runtime per flaky test. → Accepted: failed CI builds cost more than slightly slower passing builds.
- **[Risk] Defensive token refresh masks token-secret rotation issues.** If the CI refresh token itself is bad/expired, the suite would fail at the fixture's `InitializeAsync` rather than at a specific test. → Mitigation: failure message is explicit ("auth bootstrap failed: <Google error>"). Operator knows to check the secret.
- **[Trade-off] Adds two test infrastructure dependencies** (`xRetry` and the OAuth refresh helper). → Accepted: both are small, well-scoped, and the alternative is reimplementing them.
- **[Trade-off] Audit + refactor of ~10-20 existing assertion sites takes real time.** → Accepted: this is the work; there's no shortcut to better tests.

## Migration Plan

1. **Implement fixtures** first. Add `OAuthTokenFixture` and `AppReadinessFixture`. Wire into `BaseUiTest`. Existing tests still inherit from their current base (don't touch them yet).
2. **Migrate one test class** to `BaseUiTest`. Verify it still passes (and uses fresh OAuth tokens). This is the proof of concept.
3. **Migrate remaining test classes** one at a time. Each migration is small and reversible.
4. **Audit assertions** in each migrated class. Replace synchronous patterns with `Expect(...).ToBe*Async()`.
5. **Identify flaky tests** from a few CI runs (the `test-summary-reporting` summaries make this easy). Add `[RetryFact(MaxRetries = 2)]` to those specifically.
6. **Monitor for two weeks** post-merge. Track in summary whether retry-passes are stable or symptoms of a real regression.

Rollback: revert the fixture introduction + restore the original `BaseUiTest`. The Playwright assertion changes are independent — they can stay even if the fixtures revert (they're improvements regardless).

## Open Questions

- **Q1**: Should the OAuth refresh fixture cache the access token to disk for local-dev runs (where the same token can be reused across `dotnet test` invocations)? → Probably not — local dev rarely runs the suite back-to-back. Add later if pain.
- **Q2**: For the app warmup, do we want to verify a logged-in page is reachable too (catches auth-callback warmup), or is `/health` enough? → `/health` is enough for the first iteration. If we see auth-warmup flake we can extend.
- **Q3**: Should we add a "stable test" marker (opt-out of retries) for tests that should NEVER retry? → Defer. Default-no-retry, opt-in is already conservative.
