# ci-run-concurrency Specification

## Purpose
Cancel superseded in-flight CI/CD workflow runs on non-`app` branches so that the latest push wins, while preserving queue-behavior on `app` to protect production from mid-deploy cancellation and keeping the cleanup-vs-deploy mutex intact.

## Requirements

### Requirement: Subsequent push on a non-`app` branch cancels the in-flight workflow run

The deploy workflow ([.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml)) SHALL declare a workflow-level `concurrency:` block keyed per branch (`workflow-${{ github.ref }}`) with `cancel-in-progress` set to a branch-conditional expression: `true` for every branch except `app`, `false` for `app`. The result is that a new push to any non-`app` branch immediately cancels any in-flight run for the same branch — including the run's `build`, `test`, `deploy`, and `post-deployment-ui-tests` jobs.

The `app` branch SHALL preserve queue-behavior (new pushes wait for the in-flight run to complete) to protect production from mid-deploy cancellation, which could leave CloudFormation in `UPDATE_ROLLBACK_FAILED`.

#### Scenario: Two pushes to `dev` in quick succession
- **WHEN** a developer pushes commit A to `dev`, then pushes commit B to `dev` 30 seconds later (while A's workflow run is still in its `build` job)
- **THEN** GitHub Actions cancels commit A's workflow run within ~30 seconds. The run appears in the UI as "Cancelled". Commit B's workflow run starts immediately and proceeds through build, test, deploy, and UI tests for commit B's source

#### Scenario: Push to `feature/foo` does not affect `dev`'s in-flight run
- **WHEN** commit A to `dev` is in flight (e.g., deploying), and commit X to `feature/foo` is pushed
- **THEN** the two runs proceed independently because their concurrency groups (`workflow-refs/heads/dev` vs `workflow-refs/heads/feature/foo`) are distinct. Neither is canceled

#### Scenario: Two pushes to `app` queue rather than cancel
- **WHEN** a deploy to `app` is in flight and a new commit lands on `app`
- **THEN** the new commit's workflow run waits in the concurrency queue until the in-flight run completes (success or failure). Neither run is canceled. Production stack updates are never interrupted mid-flight

#### Scenario: Queued run is canceled if superseded before it starts
- **WHEN** a queued workflow run on `dev` is waiting for its predecessor to finish, and a third push lands while the second is still queued
- **THEN** GitHub Actions cancels the second (queued) run before it starts, and the third becomes the active wait — matching "latest push wins" semantics

### Requirement: Cancellation does not break the cleanup-vs-deploy mutex

The deploy workflow's existing job-level `concurrency:` block on the `deploy` job (`group: deploy-${{ matrix.region }}-${{ branch }}`, `cancel-in-progress: false`) SHALL be preserved. The `branch-stack-cleanup` capability requires the cleanup workflow to share this exact group name so it waits for any active deploy before tearing down a stack. Adding the new workflow-level cancellation does NOT change the job-level group; the cleanup contract continues to function unchanged.

When a workflow run is canceled (via the new workflow-level cancel-in-progress), all of its jobs terminate, releasing their job-level concurrency slots. The successor workflow run's deploy job then acquires the (now-released) slot. No deadlock; no concurrent deploys.

#### Scenario: Deploy job concurrency slot released on cancellation
- **WHEN** commit A's `deploy` job is mid-execution and a workflow-level cancellation triggers (because commit B was pushed)
- **THEN** the `deploy` job terminates, releasing the lock on `deploy-us-east-1-dev`. Commit B's workflow run then acquires that lock and proceeds with its own deploy

#### Scenario: Cleanup workflow still waits for in-flight deploy
- **WHEN** a deploy to `feature/foo` is in flight and the operator deletes the `feature/foo` branch
- **THEN** the cleanup workflow's concurrency group (`deploy-us-east-1-foo`) matches the deploy job's group; the cleanup waits with `cancel-in-progress: false` until the deploy completes (or is canceled by a subsequent push, which releases the lock) before starting its teardown

### Requirement: Workflow-level group is per-branch, not per-commit

The concurrency `group:` expression SHALL be `workflow-${{ github.ref }}` — keyed on the branch reference only. It SHALL NOT include the commit SHA, the matrix region, or any other dimension that would let multiple in-flight runs of the same branch coexist.

#### Scenario: Group key resolves to branch identifier
- **WHEN** a workflow runs for the `dev` branch
- **THEN** the concurrency group is `workflow-refs/heads/dev`. Every workflow run for the `dev` branch shares this group identifier regardless of commit or matrix expansion

#### Scenario: Branches with slashes in their names produce distinct groups
- **WHEN** workflows run for `feature/login-banner` and `feature/dark-mode`
- **THEN** their concurrency groups are `workflow-refs/heads/feature/login-banner` and `workflow-refs/heads/feature/dark-mode` respectively — distinct, allowing them to run in parallel without interfering
