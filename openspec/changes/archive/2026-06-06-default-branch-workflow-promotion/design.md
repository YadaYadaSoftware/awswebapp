## Context

GitHub dispatches a documented subset of workflow triggers — repository-level events such as `delete`, `schedule`, `workflow_dispatch` defaults, and others — using **only the workflow file as it exists on the repository's default branch**. This repo's default branch is `app`. The `cleanup-on-branch-delete.yml` workflow is triggered by `on: delete`, so the version that actually runs when a branch is deleted is always `app`'s — never the version on `dev` or a feature branch.

Consequence: improvements merged to `dev` (e.g. the `robust-branch-stack-cleanup` pre-cleanup steps) are integrated into the codebase but have **zero runtime effect** until `dev` is promoted to `app`. There is currently no signal that `app`'s copy is stale, so the gap is invisible until someone notices cleanup didn't behave as the latest code says it should.

CLAUDE.md already mentions this in one line under the cleanup section; this change makes it a first-class, discoverable fact and adds an automated nudge.

## Goals / Non-Goals

**Goals:**
- Make the "default-branch-only trigger" constraint impossible to miss (docs + in-file comment).
- Detect, in CI, when a default-branch-only-triggered workflow on the working branch differs from `app`, and surface it loudly on the run summary.
- Stay minimal, read-only, and domain/region-agnostic. No AWS calls, no deploy-path coupling.

**Non-Goals:**
- Changing GitHub's platform behavior (impossible).
- Auto-promoting `dev`→`app` or editing `app` from CI. Promotion stays a deliberate human merge.
- Reworking the branch model or the cleanup workflow's logic (that shipped in `robust-branch-stack-cleanup`).
- Detecting drift for `push`/`pull_request`-triggered workflows — those run from the branch under test, so they have no dormancy problem.

## Decisions

### D1. Detection by git diff against `origin/app`, scoped to delete/schedule-triggered workflows
The guard enumerates `.github/workflows/*.yml`, selects those whose `on:` block contains `delete` or `schedule`, and `git diff --quiet origin/app -- <file>` for each. Any non-identical file is reported.
- **Why git diff, not content hashing or the API:** the deploy job already checks out the repo with full history (`fetch-depth: 0`); `git fetch origin app` + `git diff` is dependency-free and exact.
- **Why scope to delete/schedule:** those are the triggers GitHub sources from the default branch; `push`/`pull_request` workflows don't have the dormancy problem, so flagging them would be noise.
- **Alternative considered:** a separate scheduled workflow that opens an issue on drift — heavier, needs `issues: write`, and (ironically) a `schedule` workflow is itself default-branch-only. Rejected for now; a summary notice in the existing run is enough.

### D2. Warn-always, fail-optionally — and never on `app` itself
- On any non-`app` branch where a watched workflow differs from `app`: write a `⚠️` block to `$GITHUB_STEP_SUMMARY` naming each drifted file and the reminder that it's dormant until promoted.
- On `dev`: additionally allow the step to **fail** (gated by a small `continue-on-error`/conditional or an input) so an integrator is reminded to promote — defaulting to warn-only to avoid blocking unrelated dev work; the failing mode is opt-in.
- On `app`: the guard is a no-op (the file on `app` IS the source of truth — there's nothing to be behind).

### D3. Live in the existing deploy workflow, not a new always-on workflow
Add the guard as one `if: always()`-style step in `zbuild.yml` (after checkout) rather than a standalone workflow. Fewer moving parts, runs on every branch's existing CI, and avoids adding another workflow that itself would be subject to the default-branch rule for some triggers.
- **Alternative:** standalone `workflow-drift-guard.yml` on `push`. Equivalent effect; chosen the inline step to minimize new files. (Either satisfies the spec.)

## Risks / Trade-offs

- **[Risk] `origin/app` not fetched in the runner** → the diff errors. → Mitigation: explicit `git fetch origin app` in the step; treat fetch failure as "skip with a note," never a hard CI failure on feature branches.
- **[Risk] False positives from trailing-whitespace / line-ending differences** → noisy warnings. → Mitigation: `git diff` already normalizes via `.gitattributes`; compare with `--ignore-cr-at-eol` to avoid CRLF noise on this Windows-authored repo.
- **[Trade-off] Warn-only by default means drift can still be ignored.** → Accepted: a visible, repeated summary notice is the right pressure for a solo-dev repo; the opt-in fail mode exists for when stricter enforcement is wanted.
- **[Trade-off] Scope limited to `delete`/`schedule`.** → Accepted: those are the cases that bite here; the watched-trigger list is a one-line edit if GitHub's behavior list changes.

## Migration Plan

1. Land docs + in-file comment + the guard step on this branch; verify the step renders the notice on a branch whose watched workflow differs from `app` (today, `cleanup-on-branch-delete.yml` differs from `app` because `robust-branch-stack-cleanup` only reached `dev`).
2. Merge to `dev`. The guard then runs on every dev/feature CI run.
3. Rollback: revert the guard step + doc edits; purely additive, no state.
