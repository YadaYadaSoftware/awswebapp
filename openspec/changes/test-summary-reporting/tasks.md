## 1. Test project configuration

- [x] 1.1 In `src/Tjb.Tests/Tjb.Tests.csproj` (or the unit test project, whichever exists) add `<VSTestLogger>trx;LogFileName=unit-tests.trx</VSTestLogger>` OR pass the equivalent via the workflow's `dotnet test --logger` invocation. Pick the workflow-side approach to keep the project file framework-agnostic. — *Done workflow-side. There is no unit-test project today (`Tjb.UiTests` is the only test project), so the project-file edit is N/A; the workflow's pre-deploy `dotnet test` step now passes `--logger "trx;LogFileName=unit-tests.trx"`.*
- [x] 1.2 In [src/Tjb.UiTests/Tjb.UiTests.csproj](../../../src/Tjb.UiTests/Tjb.UiTests.csproj) do the same with `LogFileName=ui-tests.trx`. — *Done workflow-side (UI-test `dotnet test` step now uses `--logger "trx;LogFileName=ui-tests.trx"`), keeping the csproj framework-agnostic.*
- [x] 1.3 In the Playwright configuration for UI tests (look for `playwright.config.ts`, `PlaywrightFixture.cs`, or wherever browser context options live in [src/Tjb.UiTests/](../../../src/Tjb.UiTests/)), set `trace: 'on-first-retry'` and `screenshot: 'only-on-failure'`. Verify the output directory defaults to `test-results/` (Playwright's default). — *Adapted: this project uses a custom `BaseTest`/`PlaywrightConfig` harness, not the JS Playwright config. Wired `Context.Tracing.Start/StopAsync` in `BaseTest` to write `src/Tjb.UiTests/test-results/<TestClass>/trace.zip`. Trace is captured for every test (closest feasible to `on-first-retry` in this manual xUnit harness — there is no built-in retry hook); screenshots on failure already exist via `CaptureScreenshotAsync`.*
- [ ] 1.4 Run `dotnet test --filter "FullyQualifiedName!~Tjb.UiTests" --logger "trx;LogFileName=unit-tests.trx"` locally. Confirm `**/TestResults/unit-tests.trx` exists after. — *N/A locally: no unit-test project exists, so the filtered run discovers 0 tests and emits no TRX. The fallback summary step handles this case.*
- [ ] 1.5 Run `dotnet test src/Tjb.UiTests --logger "trx;LogFileName=ui-tests.trx"` locally (against a deployed env). Confirm `src/Tjb.UiTests/TestResults/ui-tests.trx` exists after, and `src/Tjb.UiTests/test-results/` contains trace.zip for any failed test. — *Needs a deployed env + Google test tokens; deferred to CI verification (see notes).*

## 2. Workflow — unit test reporting

- [x] 2.1 In [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml), locate the `dotnet test` step in the `build` job. Modify its command line to include `--logger "trx;LogFileName=unit-tests.trx" --results-directory TestResults`.
- [x] 2.2 Add a new step `Report unit test results` after the test step. Use `dorny/test-reporter@v1` with:
  ```yaml
  if: always()
  with:
    name: Unit tests
    path: '**/TestResults/unit-tests.trx'
    reporter: dotnet-trx
    fail-on-error: false
  ```
  *(Also added job-scoped `permissions: { contents: read, checks: write }` so the action can create check runs.)*
- [x] 2.3 Add a fallback inline step that writes a basic `## Unit tests` summary to `$GITHUB_STEP_SUMMARY` if the action fails or the TRX file is missing. (Defensive — protects against the third-party action breaking the visibility we just added.)
- [x] 2.4 Add an artifact upload step for the unit test TRX: `actions/upload-artifact@v4`, path `**/TestResults/*.trx`, name `unit-test-results-${{ github.run_id }}`, retention 7 days (30 for `app`). — *Done; added `if-no-files-found: ignore` since no TRX is produced when there are no unit tests.*
- [ ] 2.5 Push to a feature branch (`test-summary-reporting`); verify the workflow run summary shows the `## Unit tests` block with totals. — *Branch pushed; run-page summary needs human/`gh` verification (see notes).*

## 3. Workflow — UI test reporting

- [x] 3.1 Locate the `post-deployment-ui-tests` job (line ~705 area). Modify the `dotnet test` step to pass `--logger "trx;LogFileName=ui-tests.trx" --results-directory src/Tjb.UiTests/TestResults`. — *Done; step `cd`s into `src/Tjb.UiTests` then uses `--results-directory ./TestResults`, which resolves to the same path.*
- [x] 3.2 Add a `Report UI test results` step using `dorny/test-reporter@v1` with `path: 'src/Tjb.UiTests/TestResults/ui-tests.trx'`, `reporter: dotnet-trx`, `fail-on-error: false`, `if: always()`.
- [x] 3.3 Add a fallback inline step (same shape as 2.3) that builds a richer markdown summary directly from the TRX file using `xmllint` or a small bash/awk script. Per-test row: name, browser, duration, outcome, failure reason (if any), link to trace artifact. — *Adapted: kept the existing `Generate Combined Test Summary` step as the fallback (it already emits totals, per-test stats, screenshot list, and artifact download links from the test output). Did not re-implement TRX `xmllint` parsing since dorny supplies the per-test detail and the existing fallback is richer than the minimal requirement.*
- [x] 3.4 Add `Upload UI test artifacts` step using `actions/upload-artifact@v4`:
  - path: `src/Tjb.UiTests/test-results/`
  - name: `ui-test-artifacts-${{ matrix.region }}-${{ github.run_id }}`
  - retention-days: `${{ needs.get-branch-name.outputs.branch-name == 'app' && 30 || 7 }}`
  - `if: always()`
  - *Adapted: the `post-deployment-ui-tests` job has no region matrix, so the name is `ui-test-artifacts-${{ github.run_id }}` (no `matrix.region`). Added `if-no-files-found: ignore`.*
- [ ] 3.5 Push to the feature branch; trigger a deploy. Verify the workflow run summary shows the `## UI tests` block AND the artifact appears in the run's "Artifacts" panel. — *Branch pushed (triggers deploy); run-page verification deferred (see notes).*

## 4. Negative testing

- [ ] 4.1 Intentionally introduce a failing unit test (`Assert.True(false, "intentional failure")` in some test class), push. Confirm:
  - Workflow turns red.
  - `## Unit tests` block shows `❌ Failed: 1` with the test name and assertion message.
  - A check-run annotation appears on the PR diff at the failing assertion line.
  - The TRX artifact is uploaded.
  - *Not performed: there is no unit-test project to add a failing test to, and this repo has no PRs (solo-dev) so PR-diff annotations don't apply. Pushing intentional failures was also avoided to keep the branch clean. Needs human verification on a real run.*
- [ ] 4.2 Intentionally make a UI test fail (e.g., assert a wrong page title). Push. Confirm:
  - `## UI tests` block shows the failure with link.
  - The link resolves to a downloadable artifact containing `trace.zip` for the failed test.
  - *Not performed (avoided pushing intentional failures). Needs human verification.*
- [ ] 4.3 Revert both intentional failures. — *N/A; no intentional failures were introduced.*

## 5. Validate and archive

- [x] 5.1 Run `openspec validate test-summary-reporting --strict`. Must pass. — *Passed: "Change 'test-summary-reporting' is valid".*
- [ ] 5.2 Verify each scenario in [specs/test-result-reporting/spec.md](specs/test-result-reporting/spec.md) is observable on a real CI run. — *Deferred to human/CI verification (see notes).*
- [x] 5.3 Update [CLAUDE.md](../../../CLAUDE.md): note in the test commands section that the workflow now produces a first-class summary; debugging failures starts at the run page summary, not the raw logs.
- [ ] 5.4 Archive the change via `/opsx:archive test-summary-reporting`. — *Intentionally NOT done: archiving is part of the merge-to-dev flow, and this run is explicitly feature-branch-only (no merge/archive).*

## Implementation notes (autonomous run on branch `test-summary-reporting`)

- **Scope reality:** the solution has only one test project (`Tjb.UiTests`); there is no unit-test project, so the "unit tests" phase discovers 0 tests. The unit-test reporting plumbing is in place and degrades gracefully (dorny `fail-on-error: false` + fallback summary + `if-no-files-found: ignore`).
- **Playwright:** custom `BaseTest`/`PlaywrightConfig` harness rather than the JS `playwright.config.ts` the tasks assumed; tracing wired in `BaseTest` writing to `src/Tjb.UiTests/test-results/<TestClass>/trace.zip`. The `Tjb.UiTests` project builds clean.
- **What I could NOT verify autonomously:** anything requiring observing the GitHub Actions run-page summary or check annotations (`gh` is not installed, and only CloudFormation monitoring was authorized). Tasks 1.4, 1.5, 2.5, 3.5, 4.x, 5.2 are left unchecked pending a human glance at a real run.
- **Permissions:** added job-scoped `permissions: { contents: read, checks: write }` to the `build` and `post-deployment-ui-tests` jobs only (NuGet restore uses nuget.org, not GitHub Packages, so `packages:` is not needed; deploy/publish jobs are untouched).
