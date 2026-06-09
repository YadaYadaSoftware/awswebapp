## 1. Prerequisite

- [x] 1.1 Confirm [`test-summary-reporting`](../test-summary-reporting/proposal.md) has been merged. Without its summary + artifact infrastructure, measuring the impact of this change is significantly harder.

## 2. Fixtures and base class

- [x] 2.1 Add `src/Tjb.UiTests/Fixtures/OAuthTokenFixture.cs`. Implements `IAsyncLifetime`. On `InitializeAsync`: reads `GOOGLE_TEST_REFRESH_TOKEN` + `GOOGLE_CLIENT_ID` + `GOOGLE_CLIENT_SECRET` env vars; POST to `https://oauth2.googleapis.com/token` with grant_type=refresh_token; stores access token + acquired-at timestamp on properties. Implements `RefreshIfStaleAsync(int maxAgeMinutes = 50)` that re-mints if token age exceeded. Throws `AuthBootstrapException` (new exception class) on failure with the upstream Google error message.
- [x] 2.2 Add `src/Tjb.UiTests/Fixtures/AppReadinessFixture.cs`. Implements `IAsyncLifetime`. On `InitializeAsync`: reads `TEST_BASE_URL` env var; polls `${url}/health` every 2 seconds with a 5-minute budget. Throws `AppNotReadyException` if budget exceeded; logs the last-seen status to help debug.
- [x] 2.3 Add `src/Tjb.UiTests/BaseUiTest.cs` (or modify the existing base class). Class declares `IClassFixture<OAuthTokenFixture>` and `IClassFixture<AppReadinessFixture>`. Constructor stores fixture references. Exposes a `Page` property with: `DefaultTimeout=30_000`, `DefaultNavigationTimeout=60_000`, the OAuth token applied as a cookie or `Authorization` header (whichever the app expects).
- [x] 2.4 Add the xUnit collection definition: `[CollectionDefinition("UiTests")]` on a marker class declaring both fixtures. Apply `[Collection("UiTests")]` to every test class so fixtures are shared.

## 3. Migrate one test class as proof of concept

- [x] 3.1 Pick the most frequently-flaky test class (consult [`test-summary-reporting`](../test-summary-reporting/proposal.md) summaries — likely `LoginNavigationTests` based on observed history). Change inheritance to `BaseUiTest`. Remove any custom `IPage` / `IBrowserContext` setup; use the base's `Page`.
- [ ] 3.2 Run the migrated tests against the deployed `dev` env: `cd src/Tjb.UiTests; dotnet test --filter "FullyQualifiedName~LoginNavigation"`. Confirm tests pass. Confirm the OAuth token is fresh (add a debug log line if needed to confirm; remove before commit).
- [ ] 3.3 Run a second time within 30 seconds — confirm fixture caches the access token, doesn't re-refresh.

## 4. Migrate remaining test classes

- [x] 4.1 List all test classes in [src/Tjb.UiTests/](../../../src/Tjb.UiTests/). For each: change inheritance to `BaseUiTest`, remove duplicate setup logic, apply `[Collection("UiTests")]`.
- [ ] 4.2 Per-class run: confirm passing.
- [ ] 4.3 Full suite run: confirm passing.

## 5. Audit assertions

- [x] 5.1 Grep `src/Tjb.UiTests/` for problematic patterns:
  - `Thread.Sleep` / `Task.Delay` in test methods
  - `Assert.Equal` / `Assert.True` / `Assert.False` against synchronous Playwright reads (e.g., `page.TitleAsync().Result`)
  - `WaitForSelector` without an explicit `Timeout` argument
- [x] 5.2 For each hit, replace with the Playwright-idiomatic `await Expect(...).ToBe*Async(new() { Timeout = 10_000 })` form. Verify the test still asserts the same thing.
- [ ] 5.3 Run the full suite. Confirm passing.

## 6. Add per-test retries for known-flaky tests

- [x] 6.1 Add `xRetry` (or `Xunit.RetryFact`) NuGet package to `src/Tjb.UiTests/Tjb.UiTests.csproj`.
- [ ] 6.2 Identify the residual flaky tests after steps 3-5. Use the most recent 10+ CI run summaries (from `test-summary-reporting`) to find tests that fail intermittently. Should be a short list (single-digit).
- [ ] 6.3 For each identified test: change `[Fact]` to `[RetryFact(MaxRetries = 2)]`. Add a comment with the observation (e.g., `// flaky 2025-12 - SSR latency on first nav`).

## 7. Observe + adjust

- [ ] 7.1 Push to a feature branch (`stabilize-ui-tests` per the new spec-branch rule). CI runs all UI tests.
- [ ] 7.2 Trigger ~5-10 dev deploys over the next week. Track in summary whether retry-passes are happening. If a test retries-passes most of the time, the root cause is unresolved — investigate further; do NOT just accept retry as the solution.
- [ ] 7.3 Adjust: tighten timeouts that proved too generous, add retries to newly-identified flaky tests, remove retries from tests that are now stable.

## 8. Validate and archive

- [x] 8.1 Run `openspec validate stabilize-ui-tests --strict`. Must pass.
- [ ] 8.2 Verify each scenario in [specs/ui-test-stability/spec.md](specs/ui-test-stability/spec.md):
  - Fixture refresh on suite start (test the OAuthTokenFixture in isolation).
  - Suite aborts cleanly when refresh fails (simulate by setting an invalid `GOOGLE_TEST_REFRESH_TOKEN`).
  - Warmup waits for `/health` (test against an intentionally-paused container).
  - Migrated tests use `Expect(...)` and pass under load.
  - Retry attribute works (induce a single-attempt failure; confirm retry recovers).
- [x] 8.3 Update [CLAUDE.md](../../../CLAUDE.md) under "Things that will trip you up": note that UI tests now use shared `OAuthTokenFixture` + `AppReadinessFixture` + retries; new tests should inherit from `BaseUiTest`.
- [ ] 8.4 Archive via `/opsx:archive stabilize-ui-tests`. Capability promoted to `openspec/specs/ui-test-stability/`.
