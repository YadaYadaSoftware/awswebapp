## ADDED Requirements

### Requirement: The default-branch-only trigger constraint is documented

The repository SHALL document, in a discoverable place, that GitHub dispatches default-branch-only workflow events (at minimum `delete` and `schedule`) using the workflow file as it exists on the default branch (`app`), so changes to such workflows on `dev` or feature branches are dormant until promoted to `app`. The documentation SHALL name the affected workflow(s) (e.g. `cleanup-on-branch-delete.yml`) and SHALL state that promotion is a deliberate `dev`→`app` merge.

#### Scenario: Constraint discoverable in project docs
- **WHEN** a contributor reads CLAUDE.md's section on branch cleanup / CI
- **THEN** a named note explains that `delete`/`schedule`-triggered workflows only take effect from the `app` default branch, and that edits elsewhere are integrated-but-dormant until promoted

#### Scenario: Constraint noted at the workflow itself
- **WHEN** a contributor opens a default-branch-only-triggered workflow file (e.g. `cleanup-on-branch-delete.yml`)
- **THEN** a header comment in that file states it only runs from the default branch and points to the fuller explanation

### Requirement: CI detects drift of default-branch-only workflows against the default branch

For every CI run on a branch other than `app`, the workflow SHALL compare each `.github/workflows/*` file whose `on:` triggers include a default-branch-only event (`delete` or `schedule`) against the same file on `app`, and SHALL report any file that differs. The comparison SHALL be read-only (it never modifies `app` or any workflow) and SHALL NOT hard-fail solely due to an inability to fetch `app` (it degrades to a skip-with-note).

#### Scenario: Drifted watched workflow surfaces a warning
- **WHEN** a CI run executes on `dev` (or a feature branch) and `cleanup-on-branch-delete.yml` differs from the copy on `app`
- **THEN** the run summary shows a clear notice naming the drifted file and stating it is dormant until promoted to `app`

#### Scenario: No drift produces no warning
- **WHEN** a CI run executes and every `delete`/`schedule`-triggered workflow is byte-identical to its `app` copy
- **THEN** the guard records "no drift" and adds no warning

#### Scenario: Guard is inert on the default branch
- **WHEN** a CI run executes on `app`
- **THEN** the drift guard performs no comparison and emits no drift warning (the `app` copy is the source of truth)

#### Scenario: Non-watched workflows are ignored
- **WHEN** a `push`/`pull_request`-only-triggered workflow differs from `app`
- **THEN** the guard does NOT report it, because such workflows run from the branch under test and are not subject to the default-branch dormancy constraint

### Requirement: Drift enforcement level is branch-appropriate

The drift guard SHALL warn (non-blocking) by default on feature branches, and SHALL support an opt-in mode that fails the run on `dev` when drift is present, so a forgotten promotion can be made blocking when desired. The guard SHALL NOT fail a run merely because watched workflows drifted on a feature branch.

#### Scenario: Feature branch warns but does not block
- **WHEN** a feature-branch CI run detects drift in a watched workflow
- **THEN** the run records the warning and the guard step still succeeds (does not fail the build)

#### Scenario: Opt-in blocking on dev
- **WHEN** the blocking mode is enabled and a `dev` CI run detects drift in a watched workflow
- **THEN** the guard step fails the run with a message identifying the drifted file(s) and instructing promotion to `app`
