## 1. Test project configuration

- [ ] 1.1 In `src/Tjb.Tests/Tjb.Tests.csproj` (or the unit test project, whichever exists) add `<VSTestLogger>trx;LogFileName=unit-tests.trx</VSTestLogger>` OR pass the equivalent via the workflow's `dotnet test --logger` invocation. Pick the workflow-side approach to keep the project file framework-agnostic.
- [ ] 1.2 In [src/Tjb.UiTests/Tjb.UiTests.csproj](../../../src/Tjb.UiTests/Tjb.UiTests.csproj) do the same with `LogFileName=ui-tests.trx`.
- [ ] 1.3 In the Playwright configuration for UI tests (look for `playwright.config.ts`, `PlaywrightFixture.cs`, or wherever browser context options live in [src/Tjb.UiTests/](../../../src/Tjb.UiTests/)), set `trace: 'on-first-retry'` and `screenshot: 'only-on-failure'`. Verify the output directory defaults to `test-results/` (Playwright's default).
- [ ] 1.4 Run `dotnet test --filter "FullyQualifiedName!~Tjb.UiTests" --logger "trx;LogFileName=unit-tests.trx"` locally. Confirm `**/TestResults/unit-tests.trx` exists after.
- [ ] 1.5 Run `dotnet test src/Tjb.UiTests --logger "trx;LogFileName=ui-tests.trx"` locally (against a deployed env). Confirm `src/Tjb.UiTests/TestResults/ui-tests.trx` exists after, and `src/Tjb.UiTests/test-results/` contains trace.zip for any failed test.

## 2. Workflow — unit test reporting

- [ ] 2.1 In [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml), locate the `dotnet test` step in the `build` job. Modify its command line to include `--logger "trx;LogFileName=unit-tests.trx" --results-directory TestResults`.
- [ ] 2.2 Add a new step `Report unit test results` after the test step. Use `dorny/test-reporter@v1` with:
  ```yaml
  if: always()
  with:
    name: Unit tests
    path: '**/TestResults/unit-tests.trx'
    reporter: dotnet-trx
    fail-on-error: false
  ```
- [ ] 2.3 Add a fallback inline step that writes a basic `## Unit tests` summary to `$GITHUB_STEP_SUMMARY` if the action fails or the TRX file is missing. (Defensive — protects against the third-party action breaking the visibility we just added.)
- [ ] 2.4 Add an artifact upload step for the unit test TRX: `actions/upload-artifact@v4`, path `**/TestResults/*.trx`, name `unit-test-results-${{ github.run_id }}`, retention 7 days (30 for `app`).
- [ ] 2.5 Push to a feature branch (`test-summary-reporting`); verify the workflow run summary shows the `## Unit tests` block with totals.

## 3. Workflow — UI test reporting

- [ ] 3.1 Locate the `post-deployment-ui-tests` job (line ~705 area). Modify the `dotnet test` step to pass `--logger "trx;LogFileName=ui-tests.trx" --results-directory src/Tjb.UiTests/TestResults`.
- [ ] 3.2 Add a `Report UI test results` step using `dorny/test-reporter@v1` with `path: 'src/Tjb.UiTests/TestResults/ui-tests.trx'`, `reporter: dotnet-trx`, `fail-on-error: false`, `if: always()`.
- [ ] 3.3 Add a fallback inline step (same shape as 2.3) that builds a richer markdown summary directly from the TRX file using `xmllint` or a small bash/awk script. Per-test row: name, browser, duration, outcome, failure reason (if any), link to trace artifact.
- [ ] 3.4 Add `Upload UI test artifacts` step using `actions/upload-artifact@v4`:
  - path: `src/Tjb.UiTests/test-results/`
  - name: `ui-test-artifacts-${{ matrix.region }}-${{ github.run_id }}`
  - retention-days: `${{ needs.get-branch-name.outputs.branch-name == 'app' && 30 || 7 }}`
  - `if: always()`
- [ ] 3.5 Push to the feature branch; trigger a deploy. Verify the workflow run summary shows the `## UI tests` block AND the artifact appears in the run's "Artifacts" panel.

## 4. Negative testing

- [ ] 4.1 Intentionally introduce a failing unit test (`Assert.True(false, "intentional failure")` in some test class), push. Confirm:
  - Workflow turns red.
  - `## Unit tests` block shows `❌ Failed: 1` with the test name and assertion message.
  - A check-run annotation appears on the PR diff at the failing assertion line.
  - The TRX artifact is uploaded.
- [ ] 4.2 Intentionally make a UI test fail (e.g., assert a wrong page title). Push. Confirm:
  - `## UI tests` block shows the failure with link.
  - The link resolves to a downloadable artifact containing `trace.zip` for the failed test.
- [ ] 4.3 Revert both intentional failures.

## 5. Validate and archive

- [ ] 5.1 Run `openspec validate test-summary-reporting --strict`. Must pass.
- [ ] 5.2 Verify each scenario in [specs/test-result-reporting/spec.md](specs/test-result-reporting/spec.md) is observable on a real CI run.
- [ ] 5.3 Update [CLAUDE.md](../../../CLAUDE.md): note in the test commands section that the workflow now produces a first-class summary; debugging failures starts at the run page summary, not the raw logs.
- [ ] 5.4 Archive the change via `/opsx:archive test-summary-reporting`.
