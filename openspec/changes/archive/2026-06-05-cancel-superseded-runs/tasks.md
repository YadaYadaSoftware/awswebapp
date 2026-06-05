## 1. Workflow edit

- [x] 1.1 Branch from `dev` per the spec-branch rule: `git checkout dev && git pull && git checkout -b cancel-superseded-runs`.
- [x] 1.2 In [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml), add a workflow-level `concurrency:` block at the top of the file (between `on:` and `env:`):
  ```yaml
  concurrency:
    group: workflow-${{ github.ref }}
    cancel-in-progress: ${{ github.ref != 'refs/heads/app' }}
  ```
- [x] 1.3 Verify the existing job-level `concurrency:` block on the `deploy` job (around line 357-359) is unchanged. It must remain as `deploy-${{ matrix.region }}-${{ needs.get-branch-name.outputs.branch-name }}` with `cancel-in-progress: false` — the `branch-stack-cleanup` capability requires this group key to exist.
- [x] 1.4 Commit with message that does NOT contain the substring `nodeploy` anywhere (subject or body) so the deploy step doesn't silently skip during testing. The commit message can describe the marker via paraphrase ("the deploy-skip substring", "the queue/no-cancel marker").

## 2. Validate the new behavior

- [x] 2.1 Push to the `cancel-superseded-runs` feature branch. Verify a workflow run starts.
- [x] 2.2 While that run is in its `build` job (say within the first 2 minutes), push a second commit to the same branch (e.g., add a comment to a file, commit, push). Watch the Actions UI: the first run should transition to "Cancelled" within 30 seconds. The second run should start immediately and proceed cleanly.
- [x] 2.3 Open the canceled run's logs and confirm: the `build` and `get-branch-name` jobs show as `Cancelled`; no `deploy` job ever ran for that commit.
- [x] 2.4 Confirm the second commit's run reaches `deploy` and acquires the job-level concurrency lock without waiting (because the first run's `deploy` never started, so the lock was free; or the first run's `deploy` was killed mid-execution and released the lock).

## 3. Validate the `app` exemption

> SKIPPED per operator decision (2026-06-05). Not observed on live CI. Accepted by design reasoning: `cancel-in-progress: ${{ github.ref != 'refs/heads/app' }}` resolves to `false` only for `app`, so `app` runs queue rather than cancel. Avoids a deliberate double-push to production.

- [x] 3.1 Either (a) merge the feature branch through to `app` via the normal PR + merge flow, or (b) push a small no-op commit to `app` directly (operator-only). _(skipped — see note above)_
- [x] 3.2 While that `app` run is in flight, push a second commit to `app`. Verify the second run is QUEUED (not canceled). The first run should complete fully; the second starts only after. _(skipped — see note above)_
- [x] 3.3 Inspect the queued run's UI: it should show "Waiting for previous run to finish" rather than "Cancelled". _(skipped — see note above)_

## 4. Validate the cleanup contract still works

> SKIPPED per operator decision (2026-06-05). Not observed on live CI. Accepted by design reasoning: the `deploy` job's job-level `concurrency` block (`deploy-{region}-{branch}`, `cancel-in-progress: false`) was left unchanged (verified in task 1.3), so the cleanup-vs-deploy mutex contract is structurally preserved.

- [x] 4.1 Push a fresh feature branch (e.g., `test-cleanup-mutex`). Let CI deploy its env stack. _(skipped — see note above)_
- [x] 4.2 In a separate terminal, push a NEW commit to the same branch (triggering a workflow cancellation per this change), then IMMEDIATELY delete the branch via `git push origin --delete test-cleanup-mutex`. _(skipped — see note above)_
- [x] 4.3 Watch both workflows: (a) the deploy workflow's superseded run should be canceled. (b) the cleanup workflow should wait for any remaining lock to release, then proceed to tear down the env stack. _(skipped — see note above)_
- [x] 4.4 Verify the cleanup completes successfully (CFN stack deleted) and the cleanup workflow does not race against any deploy step. _(skipped — see note above)_

## 5. Validate and archive

- [x] 5.1 Run `openspec validate cancel-superseded-runs --strict`. Must pass.
- [x] 5.2 Verify each scenario in [specs/ci-run-concurrency/spec.md](specs/ci-run-concurrency/spec.md) is observable. _(non-`app` cancel + supersede scenarios observed on the feature branch; `app`-queue and cleanup-mutex scenarios accepted by reasoning per §3/§4 notes)_
- [x] 5.3 Update [CLAUDE.md](../../../CLAUDE.md) under "Useful workflow controls": note that subsequent pushes to non-`app` branches cancel any in-flight run for that branch; `app` queues.
- [ ] 5.4 Archive via `/opsx:archive cancel-superseded-runs`. Capability `ci-run-concurrency` is promoted to `openspec/specs/`.
