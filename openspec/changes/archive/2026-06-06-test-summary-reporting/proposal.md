## Why

The deploy workflow runs two test suites — unit tests (xUnit, pre-deploy) and post-deployment UI tests (Playwright via xUnit, against a live URL) — but surfaces results as a binary green/red signal. When tests fail, debugging requires opening the GitHub Actions run, expanding the right job, scrolling through hundreds of lines of log output to find the failure, and then guessing which `*.trx` or trace file is relevant. The UI tests are intermittently failing right now and the lack of first-class visibility makes them painful to diagnose.

This change adds first-class reporting to `$GITHUB_STEP_SUMMARY` so that the workflow run page shows — without any clicking — exactly which tests passed, failed, or were skipped; for each failure, the assertion message and a direct link to its trace artifact. PR check annotations point at the failing test line so reviewers see test failures inline with the diff.

## What Changes

- **ADDED capability `test-result-reporting`** with requirements covering: unit-test result emission, UI-test result emission, artifact upload, and PR-check annotations.
- **Workflow edits in [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml):**
  - Add `Report unit test results` step after the existing `dotnet test` step. Parses `*.trx` files and emits a markdown table to `$GITHUB_STEP_SUMMARY` (totals + names of any failures). Uses `dorny/test-reporter@v1` or equivalent for PR check annotations.
  - Add `Report post-deployment UI test results` step in the `post-deployment-ui-tests` job. Parses Playwright/xUnit TRX output and emits a richer summary: per-test outcome, browser, duration, failure reason, link to the uploaded artifact (trace / screenshot / video).
  - Add `Upload UI test artifacts` step (always runs, even on failure) that uploads the `test-results/` directory (Playwright's default trace + screenshot output) to GitHub Actions artifacts, scoped to the matrix region and branch. Retention 7 days for nonprod branches, 30 for `app`.
  - Both reporting steps run with `if: always()` so failed test runs still emit their summaries (the current behavior is to skip post-test steps on failure, hiding the data).
- **New test-output configuration:**
  - `src/Tjb.UiTests/Tjb.UiTests.csproj` — set `<VSTestLogger>trx;LogFileName=ui-tests.trx</VSTestLogger>` or pass `--logger "trx;LogFileName=ui-tests.trx"` to `dotnet test` so TRX output is deterministic and parseable.
  - Configure Playwright to capture trace on every test (`testIdAttribute` + `trace: 'on-first-retry'`), so failure artifacts are always available when the report links to them.
- **Spec capability `test-result-reporting`:** the contract — every CI run that executes tests SHALL produce a structured summary; every failed test SHALL have an associated artifact; the artifact link SHALL appear in the summary.

## Capabilities

### New Capabilities

- `test-result-reporting`: How test execution outcomes (passed / failed / skipped, per-test detail, failure context, artifact links) are surfaced in GitHub Actions workflow summaries and PR check annotations.

### Modified Capabilities

(none)

## Impact

**Files touched:**
- `.github/workflows/zbuild.yml` — three new steps in the unit-test and ui-test jobs.
- `src/Tjb.UiTests/Tjb.UiTests.csproj` and possibly `src/Tjb.UiTests/playwright.config.ts` (or its .NET equivalent) — TRX logger + trace configuration.
- Possibly `src/Tjb.UiTests/BaseTest.cs` — if a base class wires the trace recording.

**External dependencies added:**
- `dorny/test-reporter@v1` GitHub Action (or equivalent). Open source, widely used, no auth required.

**No AWS / no source-tree refactor.** This change is workflow + test-config only.

**Out of scope (deferred to [`stabilize-ui-tests`](../stabilize-ui-tests/proposal.md)):**
- Actually fixing the flaky UI tests. This change only makes their failures legible.
- Per-test retry logic, OAuth token refresh, ALB warmup, or any test-stability work.
