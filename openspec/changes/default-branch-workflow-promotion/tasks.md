## 1. Documentation

- [x] 1.1 In [CLAUDE.md](../../../CLAUDE.md), promote the existing one-line `delete`-event note into a named gotcha: GitHub sources `delete`/`schedule`-triggered workflows from the default branch (`app`) only; edits on `dev`/feature branches are integrated but dormant until a `dev`→`app` promotion. Name the affected workflow(s). — *Done; expanded the constraint into a named "default-branch-only workflows (gotcha)" covering `delete` AND `schedule`, referencing the drift guard.*
- [x] 1.2 Add a header comment to [.github/workflows/cleanup-on-branch-delete.yml](../../../.github/workflows/cleanup-on-branch-delete.yml) stating it only runs from the default branch and pointing to the CLAUDE.md note.

## 2. CI drift guard

- [x] 2.1 Add a "Default-branch workflow drift guard" step early in the `build` job of [.github/workflows/zbuild.yml](../../../.github/workflows/zbuild.yml) (after checkout, which already uses `fetch-depth: 0`).
- [x] 2.2 In the step: `git fetch origin app` (treat fetch failure as skip-with-note, never a hard fail); enumerate `.github/workflows/*.yml`; select files whose `on:` block contains `delete` or `schedule`.
- [x] 2.3 For each watched file, `git diff --quiet --ignore-cr-at-eol origin/app -- <file>`; collect the ones that differ. No-op entirely when the current branch is `app`.
- [x] 2.4 Write results to `$GITHUB_STEP_SUMMARY`: a `⚠️` block naming each drifted file ("dormant until promoted to `app`") when drift exists, or a "no drift" line when clean.
- [x] 2.5 Enforcement: warn-only (step succeeds) on feature branches; support an opt-in blocking mode that fails on `dev` when drift exists. Default to warn-only. — *Opt-in via repo variable `DRIFT_GUARD_FAIL_ON_DEV=true`.*

## 3. Validate

- [x] 3.1 `openspec validate default-branch-workflow-promotion --strict` passes.
- [x] 3.2 Confirm the guard surfaces the expected notice on this branch (today `cleanup-on-branch-delete.yml` differs from `app`, so the guard should flag it) and is a no-op on `app`. — *Verified locally: the guard's git-diff logic flags `cleanup-on-branch-delete.yml` as drifted from `origin/app`.*
- [ ] 3.3 Push the branch; verify the drift notice renders on the run summary and the build stays green (warn-only). — *Pending the CI run on this push (monitoring).*

## 4. Archive

- [ ] 4.1 Archive the change (`/opsx:archive default-branch-workflow-promotion`); the ADDED requirements merge into `openspec/specs/default-branch-workflow-promotion/spec.md`.
