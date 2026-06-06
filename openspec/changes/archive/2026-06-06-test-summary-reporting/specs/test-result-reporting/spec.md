## ADDED Requirements

### Requirement: Every CI run that executes tests produces a markdown summary

For every workflow run that invokes `dotnet test` (whether as the pre-deploy unit-test phase or the post-deployment UI-test phase), the workflow SHALL append a structured markdown summary to `$GITHUB_STEP_SUMMARY`. The summary SHALL include at minimum:

- Suite name (Unit tests / UI tests)
- Total count: passed, failed, skipped
- For each failed test: fully-qualified test name and the failure reason (assertion message, exception message, or test runner message)

The summary step SHALL run with `if: always()` so it executes even when the test step itself returned a non-zero exit code.

#### Scenario: Unit test summary appears on successful run
- **WHEN** a push to any branch triggers the workflow and unit tests pass (e.g., 42 passed, 0 failed, 0 skipped)
- **THEN** the workflow run page shows a `## Unit tests` section in its summary with the totals, and the build job completes green

#### Scenario: Unit test summary on failed run
- **WHEN** a unit test fails (e.g., `Tjb.Data.Tests.RepositoryTests.CanLoadProjects` throws an assertion)
- **THEN** the workflow run page shows the `## Unit tests` section with `❌ Failed: 1` and a table row naming `Tjb.Data.Tests.RepositoryTests.CanLoadProjects` and its failure reason. The build job exits non-zero, but the summary is captured

#### Scenario: UI test summary on successful run
- **WHEN** a deploy completes and the post-deployment UI tests all pass
- **THEN** the workflow run page shows a `## UI tests` section with per-test outcomes (test name, browser, duration). No "Failures" subsection appears

#### Scenario: UI test summary on failed run
- **WHEN** one or more post-deployment UI tests fail
- **THEN** the workflow run page shows the `## UI tests` section with a "Failures" subsection listing each failed test, the failure reason, and a markdown link to the test's trace/screenshot artifact uploaded in the same run

### Requirement: Failure artifacts are always uploaded and linked from the summary

For every CI run that executes UI tests, the workflow SHALL upload the `test-results/` directory (containing Playwright traces and screenshots for any failed tests) as a GitHub Actions artifact. The upload step SHALL run with `if: always()`. The summary's per-test failure entries SHALL include a relative link to the artifact name so a reader can download it from the run page in one click.

For unit tests, the TRX log file SHALL be uploaded as an artifact (without per-test artifacts, since unit tests don't produce trace/screenshot output). Artifact retention SHALL be 7 days for branches other than `app`, and 30 days for `app`.

#### Scenario: Failed UI test has trace artifact
- **WHEN** a UI test `LoginNavigationTests.SignInWithGoogle` fails on the first attempt
- **THEN** the workflow uploads a `test-results/` artifact containing at minimum `LoginNavigationTests.SignInWithGoogle/trace.zip` and `LoginNavigationTests.SignInWithGoogle/screenshot.png`. The summary's failure entry includes a link of the form `[Trace](artifacts/test-results)` resolvable from the run page

#### Scenario: Artifact retention by branch
- **WHEN** a workflow run on the `dev` branch uploads test artifacts
- **THEN** the artifacts are retained for 7 days. The same upload on the `app` branch retains for 30 days

#### Scenario: Artifact uploaded on test-step failure
- **WHEN** the `dotnet test` step itself exits non-zero (tests failed, OR the runner crashed)
- **THEN** the artifact upload step still runs (because of `if: always()`) and the partial `test-results/` directory is preserved

### Requirement: PR check annotations point at failing test source lines

For pull requests that trigger the workflow, failed unit tests SHALL produce GitHub check-run annotations that link to the test source file and line in the PR diff view. The annotation SHALL include the test name and the failure reason. Implementation uses `dorny/test-reporter@v1` (or an equivalent action that produces check-run annotations from TRX input).

UI-test failure annotations are NOT required for PR check views, since the test source line is typically not what a reviewer needs to look at (the failure is usually a product issue, not a test-code issue).

#### Scenario: Failed unit test annotated on PR
- **WHEN** a PR is open and a unit test in the PR's diff fails
- **THEN** the PR's "Files changed" tab shows an inline annotation on the failing test source line, with the failure message visible without leaving the PR

#### Scenario: PR with no test failures
- **WHEN** a PR's workflow run completes with all unit tests passing
- **THEN** no check-run annotations are added; the existing green check on the PR is the only signal

### Requirement: TRX output is deterministic

The `dotnet test` invocations for both unit and UI suites SHALL pass `--logger "trx;LogFileName=<fixed-name>.trx"` so the TRX output file lands at a known path. The reporting step SHALL read from that known path. Specifically:

- Unit tests: `unit-tests.trx`
- UI tests: `ui-tests.trx`

#### Scenario: TRX file at known path
- **WHEN** `dotnet test --logger "trx;LogFileName=unit-tests.trx"` runs against `src/Tjb.Tests/`
- **THEN** a file `TestResults/unit-tests.trx` exists at the project's `TestResults/` directory and contains the test outcomes in TRX XML format

### Requirement: Playwright captures trace and screenshot on failure

The UI test project's Playwright configuration SHALL set:

- `trace: 'on-first-retry'` — capture full Playwright trace (DOM snapshots, network log, console) when a test fails its first attempt.
- `screenshot: 'only-on-failure'` — capture a single screenshot at the failure moment.

These artifacts land under `test-results/<test-name>/` and are picked up by the artifact upload step.

#### Scenario: Trace captured on test failure
- **WHEN** a Playwright-based UI test fails its first attempt
- **THEN** the project's `test-results/` directory contains a `trace.zip` for that test
