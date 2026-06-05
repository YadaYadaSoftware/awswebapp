## Context

The deploy workflow [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) currently has one concurrency control: a job-level block on the `deploy` job:

```yaml
concurrency:
  group: deploy-${{ matrix.region }}-${{ needs.get-branch-name.outputs.branch-name }}
  cancel-in-progress: false
```

This queues parallel deploys for the same region+branch. It does NOT queue or cancel the upstream `build` job, the `get-branch-name` job, or the downstream `post-deployment-ui-tests` job. So in the current model, two pushes to `dev` in quick succession:

1. Both trigger workflow runs.
2. Both runs' `build` jobs run in parallel (wasted compute).
3. The first deploy job acquires the per-region+branch lock; the second waits.
4. After the first completes its deploy, the second's deploy proceeds.
5. Both runs' `post-deployment-ui-tests` jobs run (the second one runs against the SECOND deploy, but is still serialized by its own job).

End result: the older commit's results land green/red AFTER the newer commit's results, often misleading the developer about which commit is being tested.

This change replaces the job-level lock with a workflow-level cancel-in-progress lock, scoped per branch, with `app` exempted.

## Goals / Non-Goals

**Goals:**
- A new push to any non-`app` branch cancels any in-flight run of the same workflow on that branch.
- `app` pushes preserve queue-behavior (no cancellation) to protect production deploys from mid-flight cancellation.
- Cancellation scope is per-branch — branches don't interfere with each other.
- The CI run UI shows only the latest commit's outcome (the older run shows as "Cancelled").
- Simpler concurrency model: one workflow-level block, no job-level overrides.

**Non-Goals:**
- Cancellation of the `cleanup-on-branch-delete.yml` workflow. It runs on `delete` events; the same branch can only be deleted once.
- Cancellation across different workflows. All testing/deploy is in one workflow today.
- A workflow input or repo variable that toggles the behavior. The branch-conditional logic is enough.
- Cancellation of CFN stack operations themselves. GitHub cancels the workflow step; CFN keeps running whatever changeset it has in flight. CloudFormation's own rollback semantics handle the consequences.
- Per-job opt-out. The entire workflow run is canceled or it isn't — `build` and `deploy` and `post-deployment-ui-tests` move together.

## Decisions

### D1. Workflow-level concurrency block, per-branch group

```yaml
concurrency:
  group: workflow-${{ github.ref }}
  cancel-in-progress: ${{ github.ref != 'refs/heads/app' }}
```

- **Group key `workflow-${{ github.ref }}`**: per branch (e.g., `workflow-refs/heads/dev`). Different branches have independent groups, so a `feature/foo` push doesn't cancel a `dev` deploy.
- **`cancel-in-progress: ${{ github.ref != 'refs/heads/app' }}`**: expression evaluates to `true` for every branch except `app`. The result is a boolean per run, set at workflow-trigger time.

**Why workflow-level over job-level**: a job-level block only affects the specified job. The `build` job's compute is wasted on the older commit (it still completes before being passed to the now-canceled deploy). Workflow-level cancellation kills everything — build, deploy, UI tests.

**Why per-branch (not per-branch+region)**: from the deploy job's perspective, the two regions are coordinated (the Aurora global cluster wires them together). Canceling one region while the other proceeds creates a half-deployed state that's worse than canceling both. The simpler per-branch grouping matches "one commit's deploy as a unit."

### D2. Keep the existing job-level concurrency block

Initial draft of this design proposed removing the existing `deploy` job's `concurrency: { group: deploy-${{ region }}-${{ branch }}, cancel-in-progress: false }` as redundant. That was wrong: the `branch-stack-cleanup` capability (in `openspec/specs/branch-stack-cleanup/spec.md`) explicitly requires the cleanup workflow to share that exact group so cleanup waits for any active deploy before tearing down. Removing the job-level block would break the cleanup-vs-deploy mutex.

Both blocks coexist cleanly:
- **Workflow-level** handles cancel-on-supersede for the entire run.
- **Job-level** preserves the cleanup-mutex contract.

When a workflow run is canceled (by workflow-level cancel-in-progress), all of its jobs are terminated, releasing their job-level locks. The next run's deploy job then acquires the lock. No deadlock; no concurrent deploys. The cleanup workflow's wait-for-deploy contract is preserved.

### D3. App branch exemption uses the expression form

GitHub Actions concurrency supports expressions in `cancel-in-progress` as of late 2023:

```yaml
cancel-in-progress: ${{ github.ref != 'refs/heads/app' }}
```

Evaluated at workflow-trigger time. For `app`, the expression resolves to `false`; for everything else, `true`. No conditional `if:` needed at the step level.

**Alternative considered**: two separate workflow files, one for `app` (no cancel), one for others (cancel). Rejected — would duplicate the entire build/test/deploy pipeline. Forking workflows for a single config bit is excessive.

**Alternative considered**: branch-level concurrency settings via the workflow's matrix or a job-level if-conditional. Rejected — concurrency blocks live at workflow-or-job scope, not matrix scope. Conditional skip-jobs would still leave the unwanted runs visible in the UI.

### D4. Cleanup workflow concurrency contract is preserved by D2

Because we keep the job-level concurrency block on the `deploy` job (per D2), the cleanup workflow's existing concurrency group (which references the same group name `deploy-us-east-1-{branch-leaf}`) continues to function. No change to the cleanup workflow or to the `branch-stack-cleanup` capability spec is required.

### D5. Cancellation semantics for in-flight AWS operations

When GitHub cancels a workflow run mid-step, the step's process is killed. Any AWS CLI call in flight may complete, fail, or hang depending on timing:

- A `aws cloudformation deploy` that has already initiated a stack update will keep updating the stack — CFN doesn't know GitHub canceled the calling process. The next workflow run will see the stack in `UPDATE_IN_PROGRESS` and wait for it to settle.
- A `aws s3 cp` upload may leave a partial object; the next run re-uploads from scratch.
- A `aws ecr push` may leave a layer half-uploaded; ECR's content-addressable layer store handles dedup on retry.

None of these are catastrophic; CFN and S3 and ECR all converge eventually. The cancellation pattern is well-trodden in GitHub Actions community usage — accepted risk.

## Risks / Trade-offs

- **[Risk] Cancellation mid-CFN-deploy on a non-app branch leaves the stack in `UPDATE_IN_PROGRESS`.** The next workflow run waits for it to settle (CFN-internal time, usually <5 min). → Accepted: idempotency of the next run handles this. Worst case the next run hits "stack still updating" and retries.

- **[Risk] If the developer pushes rapidly to `dev` for hours, every push cancels its predecessor, and no commit actually completes a deploy.** → Mitigation: this is a self-inflicted DOS that the developer can spot in the run UI. No code-level fix.

- **[Risk] `app` queue grows arbitrarily** if multiple pushes land on `app` while a slow deploy is in flight. → Accepted: rare in practice; PRs to `app` are deliberate, infrequent.

- **[Trade-off] Build/test compute is "wasted" if a push is canceled.** Pre-change, build always finished even when deploy was queued; the build artifact landed for the wrong commit. Post-change, build is canceled too. → Accepted: this is exactly what "latest push wins" means. Wasted compute on a never-deployed build is better than green checks on a never-deployed commit.

## Migration Plan

- **Step 1**: branch from `dev` per the spec-branch rule (branch name: `cancel-superseded-runs`).
- **Step 2**: edit `.github/workflows/zbuild.yml` to add the workflow-level `concurrency:` block at the top. Leave the deploy job's existing job-level concurrency block unchanged.
- **Step 3**: push to the feature branch. Manually trigger a second push within 30 seconds. Verify in the GitHub Actions UI that the first run is canceled and the second proceeds cleanly.
- **Step 4**: merge to `dev`. Verify same behavior on dev. Then through to `app` for a full validation (where the workflow-level expression evaluates to `false` and queue-behavior is preserved).
- **Step 5**: archive the change.

Rollback: revert the workflow edits. Original queue-behavior restored.

## Open Questions

- **Q1**: Should `beta` and `alpha` (shared infra branches) be treated like `app` (queue, not cancel)? They're not production but they're shared resources. → Tentative: cancel for them. They're meant to mirror prod's behavior for testing but the cost of a canceled mid-deploy is identical to dev — a stuck CFN update that the next run sorts out. Easy to revisit if it bites.
- **Q2**: Does GitHub Actions cancel a workflow that's WAITING in a concurrency queue (vs running)? → Yes — a queued run that's superseded by a newer push is canceled before it ever starts. Matches our intent.
- **Q3**: What about pull_request events that retrigger on every push to the PR branch? → Same behavior. PR-triggered runs share the workflow-level group keyed on `github.ref` (which is the PR's head ref for pull_request events).
