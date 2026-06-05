## Context

The deploy workflow has two test phases:

1. **Pre-deploy unit tests** in the `build` job — `dotnet test --filter "FullyQualifiedName!~Tjb.UiTests"`. Run on every push. If they fail, deploy is blocked.
2. **Post-deployment UI tests** in the `post-deployment-ui-tests` job — Playwright/xUnit running against the deployed URL (`https://<branch-leaf>.{domain}`). Run after a successful deploy, only on non-`nodeploy` runs.

Currently, both surface results as job exit code only. Failures require expanding the job, finding the `Run dotnet test` step, scrolling through hundreds of lines of build output to locate the failure line. For UI tests, the failure root cause is usually a Playwright screenshot or trace file — but those aren't currently uploaded as artifacts, so they're invisible.

This change makes results legible without leaving the run page.

## Goals / Non-Goals

**Goals:**
- Every CI run that executes tests produces a markdown summary at `$GITHUB_STEP_SUMMARY` showing totals and failure detail.
- Every failed test has an artifact (TRX log at minimum; Playwright trace/screenshot for UI tests) and the summary links to it.
- PR check annotations point reviewers at failing test lines.
- Summary persists even when the test step itself fails (current behavior swallows the diagnostic when the job is red).
- Output is deterministic and parseable (fixed TRX filenames, fixed artifact paths).

**Non-Goals:**
- Fixing flaky tests. Diagnosis only — `stabilize-ui-tests` is the sibling change.
- Test analytics / historical dashboards (e.g., Datadog Test Visibility, Allure). The native GitHub summary is enough for this team's scale.
- Custom in-house test report renderer. Use `dorny/test-reporter` or equivalent — proven, maintained.
- Slack / email notifications. The summary lives where the developer is already looking (PR check).
- Reporting build / lint / format warnings. Tests only.

## Decisions

### D1. Use `dorny/test-reporter@v1` for the PR check annotations

`dorny/test-reporter` ingests TRX/JUnit/NUnit/Mocha/etc., renders a markdown summary into the workflow run page, creates a separate "check run" with annotations linked to test source lines, and surfaces failures inline on PR diffs.

- **Why over hand-rolling**: the action handles TRX parsing, summary formatting, and check-run API integration. Writing this ourselves is ~200 lines of Python/Node for no benefit.
- **Why over `EnricoMi/publish-unit-test-result-action`**: dorny supports TRX natively (xUnit's default output); EnricoMi requires JUnit. Both work; dorny matches our existing test runner.
- **Risk**: third-party dependency. Pinned to `@v1` (major version) so a breaking change requires deliberate upgrade.

### D2. Always-run with `if: always()`

Both reporting steps SHALL use `if: always()` so they run even when the test step itself returned non-zero. Without this, the post-test reporting is skipped on the only runs where it actually matters.

### D3. Markdown summary written via `$GITHUB_STEP_SUMMARY`

A dedicated `Append unit test summary` step writes a markdown block to `$GITHUB_STEP_SUMMARY` (a GitHub-provided file path). Format:

```
## Unit tests
- ✅ Passed: 42
- ❌ Failed: 1
- ⏭ Skipped: 0

### Failures
| Test | Reason |
| --- | --- |
| `Tjb.Data.Tests.RepositoryTests.CanLoadProjects` | Expected 3, got 2 |
```

The `dorny/test-reporter` action handles this internally. Our explicit step is a fallback if the action is unavailable or for the UI-test job which has richer requirements (per-browser, artifact links).

### D4. Playwright trace + screenshot configured for failure capture

Two settings in the Playwright config (or .NET `LaunchOptions`):

- `trace: 'on-first-retry'` — captures full trace (DOM snapshots, network log, console) when a test fails its first attempt. Cheaper than `'on'` (every test) but still useful for diagnosing flakes.
- `screenshot: 'only-on-failure'` — captures a single screenshot at the moment of failure.

Both write to `test-results/` (Playwright's default). The artifact upload step picks up the whole directory.

`video: 'retain-on-failure'` is **not** enabled by default — videos are large (~MB per test) and the static screenshot + trace usually tells the story. Operator can enable if needed.

### D5. Artifact retention

- Nonprod branches (dev / beta / alpha / feature branches): 7 days. Short enough to keep storage cheap, long enough to debug yesterday's failure.
- `app` (production): 30 days. Production failures sometimes get investigated later (compliance, post-incident).

Configured via `actions/upload-artifact@v4` `retention-days` input, branched on `${{ needs.get-branch-name.outputs.branch-name == 'app' }}`.

### D6. TRX logger configured deterministically

`dotnet test` defaults to a random filename for TRX output (`<machine>_<user>_<datetime>.trx`). For the report step to find the file, we pin the filename:

```
dotnet test --logger "trx;LogFileName=unit-tests.trx"
```

For UI tests, same pattern with a different filename (`ui-tests.trx`). The report step reads from a known path.

### D7. Job-level fail-fast vs summary-first

The unit test job (`build`) currently exits non-zero on test failure, which fails the workflow and prevents deploy. This stays as-is — failing unit tests should block deploy. But the report step runs BEFORE the job exit so the summary is captured.

For the UI test job, failure currently doesn't block anything downstream (it's the last job). Adding reporting doesn't change that — failures will still be visible in summary but the workflow run is still red.

## Risks / Trade-offs

- **[Risk] `dorny/test-reporter@v1` is a third-party action.** → Mitigation: pinned to major version, widely used (~5M+ runs/month per GitHub Marketplace), source available, can be vendored if maintenance ever lapses.
- **[Risk] TRX parsing fails on malformed output (e.g., test runner crashed mid-run).** → Mitigation: the action handles malformed input gracefully and falls back to a "results unavailable" summary; the workflow step itself does not throw on parse failure (`fail-on-error: false`).
- **[Risk] Artifact storage costs accumulate.** → Mitigation: retention limits (7/30 days). At current test volume (~50 tests, ~100 KB of artifacts per run), even daily failures for a year are ~250 MB.
- **[Trade-off] Summary is markdown only — not interactive.** No drill-down beyond what's in the markdown table. → Accepted: GitHub Actions UI is the constraint. Linking to trace artifacts gives the interactive experience.
- **[Trade-off] Adds ~30-60s to workflow runtime** (action setup + TRX parse + artifact upload). → Accepted: tests already take many minutes; this is in the noise.

## Migration Plan

This change is workflow-only — no AWS state, no application code. Migration is just merging the change.

- **Step 1**: edit `.github/workflows/zbuild.yml` to add the new steps. Edit `src/Tjb.UiTests/` config to pin TRX filename and enable trace/screenshot capture.
- **Step 2**: push to a feature branch (per the new spec-branch rule — branch name = `test-summary-reporting`). CI runs end-to-end. Verify the summary appears on the run page and links resolve.
- **Step 3**: merge to `dev`. Verify dev's automated deploy + UI test runs surface the new summary.
- **Step 4**: archive the change once verified working.

Rollback: revert the workflow + test-config commits. Old behavior is fully restored.

## Open Questions

- **Q1**: Should the UI test job get richer per-browser breakdown (Chromium vs Firefox vs WebKit) if/when we expand browser coverage? Currently tests only run in one browser. → Defer; add when/if multi-browser coverage lands.
- **Q2**: Do we want a "test history" view across runs (e.g., a test that fails 4/10 last week)? → Out of scope; native GitHub Actions doesn't support this. Would require a 3rd-party service.
- **Q3**: Should failed-test summaries also be posted as PR comments? → Defer; the check-run annotations are already inline with the diff.
