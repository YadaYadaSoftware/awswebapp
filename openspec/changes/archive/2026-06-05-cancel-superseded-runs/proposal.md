## Why

The deploy workflow currently queues subsequent pushes on the same branch behind any in-flight run (`cancel-in-progress: false` on the deploy job's concurrency group). When a developer pushes two commits in quick succession to `dev`, the second push waits for the first's full deploy + UI test cycle to finish before starting — typically 10-20 minutes of wasted compute and wasted developer attention on the older commit's outcome. The CI run UI lights up green or red based on the OLD commit, which is misleading.

The right behavior for a developer iteration loop: a new push supersedes the older one. Build, test, deploy, and UI tests for the older commit get canceled; the workflow for the latest commit takes over.

Production (`app` branch) is the exception. A cancellation mid-deploy on prod can leave CloudFormation in `UPDATE_ROLLBACK_FAILED` requiring manual intervention. For prod, queueing is the conservative choice — each commit's deploy completes (success or rollback) before the next starts.

## What Changes

- **ADDED capability `ci-run-concurrency`** with the requirement that a new push on a non-`app` branch cancels any in-flight workflow run for the same branch.
- **Workflow change in [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml):** add a workflow-level `concurrency:` block scoped per `github.ref`. The `cancel-in-progress` field is conditional on the branch:
  ```yaml
  concurrency:
    group: workflow-${{ github.ref }}
    cancel-in-progress: ${{ github.ref != 'refs/heads/app' }}
  ```
  - **Per-branch scope** (group is `workflow-${{ github.ref }}`): a new push to `dev` only cancels other in-flight `dev` runs, not a parallel `feature/foo` run.
  - **Workflow-level** (not job-level): cancellation applies to the entire run — build, test, deploy, UI tests — not just the deploy job. The older commit's outdated build artifacts are not produced or published.
  - **Conditional cancel** (`!= 'refs/heads/app'`): `app` evaluates to `cancel-in-progress: false` (queue); every other branch evaluates to `true` (cancel).
- **The existing job-level concurrency on the `deploy` job stays in place** (`deploy-${{ matrix.region }}-${{ branch }}`, `cancel-in-progress: false`). It's not redundant — the `branch-stack-cleanup` capability requires the cleanup workflow to share this exact group name so it waits for any active deploy before tearing down. Removing the job-level block would break that contract. With the workflow-level cancel-in-progress in place AND the job-level lock preserved, the system works as:
  1. New push → workflow-level cancel kills the previous run (jobs of the previous run are terminated, releasing the job-level lock).
  2. New run starts; its deploy job acquires the (now-released) job-level lock.
  3. A cleanup workflow invocation continues to wait on the job-level lock as before.
- **No modification to `branch-stack-cleanup` is required.** Its contract about the cleanup-vs-deploy mutex is preserved.

## Capabilities

### New Capabilities

- `ci-run-concurrency`: How the deploy workflow handles concurrent runs for the same branch — superseded runs are canceled for non-prod branches; production runs queue.

### Modified Capabilities

(none)

## Impact

**Files touched:**
- `.github/workflows/zbuild.yml` — add workflow-level `concurrency:` block at the top; remove redundant job-level block on the `deploy` job.

**Developer experience:**
- A push that supersedes an in-flight run cancels that run within ~30 seconds (GitHub Actions cancellation propagation). The UI shows the older run as "Cancelled" and the new run starts immediately.
- For `app`: behavior unchanged — pushes queue.

**No AWS state changes. No application code changes. No external dependencies.**

**Out of scope (deferred):**
- Cancellation for the cleanup workflow on branch-delete events (it doesn't have a same-branch race because branches can only be deleted once).
- Per-job cancellation policies (e.g., letting build complete but canceling deploy). The all-or-nothing workflow-level cancel is the right primitive for "latest push wins."
- Configurable cancellation behavior via a workflow input. The branch-conditional logic is enough — no operator knob needed.
- Cancellation across different workflows (e.g., the post-deployment UI test workflow as a separate workflow). All current testing lives in `zbuild.yml` as jobs of the same workflow, so workflow-level concurrency naturally covers it.
