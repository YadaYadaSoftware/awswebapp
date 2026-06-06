## Why

GitHub only dispatches repository-level workflow events — notably `delete` (and `schedule`) — for workflow files that live on the **default branch** (`app`). So when an improvement to a delete-triggered workflow (e.g. [.github/workflows/cleanup-on-branch-delete.yml](../../../.github/workflows/cleanup-on-branch-delete.yml), which tears down a branch's CloudFormation stack) is merged to `dev`, it is integrated but **dormant** — it has no real-world effect until a `dev`→`app` promotion. Worse, nothing surfaces when `app`'s copy of such a workflow has drifted behind `dev`, so a fix can silently sit un-activated for a long time (the recent `robust-branch-stack-cleanup` pre-cleanup logic is exactly this situation today).

## What Changes

- **Document the constraint** prominently so it stops surprising people:
  - A note in [CLAUDE.md](../../../CLAUDE.md) (it already mentions the `delete`-event constraint in passing; make it a first-class, named gotcha covering `delete` **and** `schedule`).
  - A short header comment in each default-branch-only-triggered workflow file pointing at the constraint.
- **Add a lightweight CI drift guard.** A step/job in the existing deploy workflow (or a tiny standalone workflow) compares each default-branch-only-triggered workflow file (those whose `on:` includes `delete` or `schedule`) on the current branch against the copy on `app`. When they differ, it writes a clear **"⚠️ dormant until promoted to `app`"** notice to `$GITHUB_STEP_SUMMARY` listing the drifted files. On `dev` it MAY fail (configurable) so a promotion isn't forgotten; on feature branches it only warns.
- The guard is **detection-only** — it never edits `app` or auto-promotes. Promotion stays a deliberate human `dev`→`app` merge.

## Capabilities

### New Capabilities
- `default-branch-workflow-promotion`: How workflows triggered by default-branch-only GitHub events (`delete`, `schedule`) are kept current on the default branch, how the "dormant until promoted" constraint is documented, and how drift between a working branch and `app` is detected and surfaced in CI.

### Modified Capabilities
<!-- none — no existing capability's requirements change -->

## Impact

- **Docs:** [CLAUDE.md](../../../CLAUDE.md) gains a named "default-branch-only workflows" gotcha; header comments added to the affected workflow file(s).
- **CI:** [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) gains a small "default-branch workflow drift" guard step (or a new minimal `.github/workflows/workflow-drift-guard.yml`). Read-only — uses `git` to diff against `origin/app`; no AWS, no deploy-path change.
- **No application, infrastructure-template, or AWS-resource changes.** Domain/region-agnostic per the repo conventions.
- **Out of scope:** changing GitHub's platform behavior, auto-promoting to `app`, or reworking the branch model.
